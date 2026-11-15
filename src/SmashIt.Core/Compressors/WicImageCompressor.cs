using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace SmashIt.Core.Compressors;

/// <summary>
/// Re-encodes images with the Windows Imaging Component (built into Windows, no extra binary).
/// HEIF and WebP/AVIF decoding rely on the free "HEIF / HEVC / WebP / AV1 Image Extensions" from the Store.
/// </summary>
/// <remarks>
/// Pixels are decoded with the EXIF orientation applied and colors converted to sRGB, so the result looks
/// identical even though metadata is not carried over yet. 16-bit images are refused in lossless mode
/// because they would be reduced to 8 bits per channel.
/// </remarks>
public sealed class WicImageCompressor : ICompressor
{
    private static readonly HashSet<MediaFormat> Readable =
    [
        MediaFormat.Jpeg, MediaFormat.Png, MediaFormat.Gif, MediaFormat.Bmp, MediaFormat.Tiff,
        MediaFormat.JpegXr, MediaFormat.Heif, MediaFormat.WebP, MediaFormat.Avif,
    ];

    private static readonly Dictionary<MediaFormat, Guid> Encoders = new()
    {
        [MediaFormat.Jpeg] = BitmapEncoder.JpegEncoderId,
        [MediaFormat.Png] = BitmapEncoder.PngEncoderId,
        [MediaFormat.Bmp] = BitmapEncoder.BmpEncoderId,
        [MediaFormat.Tiff] = BitmapEncoder.TiffEncoderId,
        [MediaFormat.JpegXr] = BitmapEncoder.JpegXREncoderId,
        [MediaFormat.Heif] = BitmapEncoder.HeifEncoderId,
    };

    public string Id => "wic";

    public string DisplayName => "Windows Imaging Component";

    public bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options)
    {
        if (!Readable.Contains(input) || !Encoders.ContainsKey(output)) return false;

        // Re-encoding a JPEG/HEIC is never lossless: that needs jpegtran-style tools.
        var sameFormat = input == output;
        if (sameFormat && options.Mode == CompressionMode.Lossless && MediaFormatInfo.IsLossyOnly(output))
            return false;

        // Re-encoding a lossless file to the same format in lossy mode would gain nothing with WIC.
        if (sameFormat && options.Mode == CompressionMode.Lossy && output is MediaFormat.Bmp)
            return false;

        return true;
    }

    public async Task CompressAsync(
        string inputPath,
        string outputPath,
        MediaFormat outputFormat,
        CompressionOptions options,
        CancellationToken cancellationToken)
    {
        await using var input = new FileStream(inputPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var inputStream = input.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(inputStream).AsTask(cancellationToken);

        if (decoder.FrameCount > 1)
            throw new NotSupportedException("Les images à plusieurs images (GIF animé, TIFF multipage) ne sont pas encore prises en charge.");

        if (options.Mode == CompressionMode.Lossless && decoder.BitmapPixelFormat is BitmapPixelFormat.Rgba16 or BitmapPixelFormat.Gray16)
            throw new NotSupportedException("Image 16 bits : la réencoder en 8 bits ne serait pas sans perte.");

        var alphaMode = SupportsAlpha(outputFormat) ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore;
        var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                alphaMode,
                new BitmapTransform(),
                ExifOrientationMode.RespectExifOrientation,
                ColorManagementMode.ColorManageToSRgb)
            .AsTask(cancellationToken);
        var pixels = pixelData.DetachPixelData();

        cancellationToken.ThrowIfCancellationRequested();

        await using var output = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var outputStream = output.AsRandomAccessStream();
        var encoderOptions = BuildEncoderOptions(outputFormat, options);
        var encoder = encoderOptions.Count > 0
            ? await BitmapEncoder.CreateAsync(Encoders[outputFormat], outputStream, encoderOptions).AsTask(cancellationToken)
            : await BitmapEncoder.CreateAsync(Encoders[outputFormat], outputStream).AsTask(cancellationToken);

        encoder.SetPixelData(
            BitmapPixelFormat.Bgra8,
            alphaMode,
            decoder.OrientedPixelWidth,
            decoder.OrientedPixelHeight,
            decoder.DpiX,
            decoder.DpiY,
            pixels);

        await encoder.FlushAsync().AsTask(cancellationToken);
    }

    private static bool SupportsAlpha(MediaFormat format) =>
        format is MediaFormat.Png or MediaFormat.Tiff or MediaFormat.JpegXr or MediaFormat.Heif;

    private static BitmapPropertySet BuildEncoderOptions(MediaFormat format, CompressionOptions options)
    {
        var set = new BitmapPropertySet();
        var quality = Math.Clamp(options.Quality, 1, 100) / 100f;
        var lossy = options.Mode == CompressionMode.Lossy || MediaFormatInfo.IsLossyOnly(format);

        switch (format)
        {
            case MediaFormat.Jpeg:
                set["ImageQuality"] = new BitmapTypedValue(quality, PropertyType.Single);
                // WICJpegYCrCbSubsamplingOption: 1 = 4:2:0 (smaller), 3 = 4:4:4 (sharper colors).
                set["JpegYCrCbSubsampling"] = new BitmapTypedValue((byte)(quality < 0.9f ? 1 : 3), PropertyType.UInt8);
                break;
            case MediaFormat.Heif:
                set["ImageQuality"] = new BitmapTypedValue(quality, PropertyType.Single);
                break;
            case MediaFormat.JpegXr:
                if (lossy) set["ImageQuality"] = new BitmapTypedValue(quality, PropertyType.Single);
                else set["Lossless"] = new BitmapTypedValue(true, PropertyType.Boolean);
                break;
            case MediaFormat.Png:
                // WICPngFilterAdaptive: picks the best filter per scanline.
                set["FilterOption"] = new BitmapTypedValue((byte)6, PropertyType.UInt8);
                break;
            case MediaFormat.Tiff:
                // WICTiffCompressionZIP (Deflate), lossless.
                set["TiffCompressionMethod"] = new BitmapTypedValue((byte)6, PropertyType.UInt8);
                break;
        }

        return set;
    }
}
