using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Imaging;
using SmashIt.Core.Metadata;
using Windows.Foundation;
using Windows.Graphics.Imaging;

namespace SmashIt.Core.Compressors;

/// <summary>
/// Re-encodes images with the Windows Imaging Component (built into Windows, no extra binary).
/// HEIF and WebP/AVIF decoding rely on the free "HEIF / HEVC / WebP / AV1 Image Extensions" from the Store.
/// </summary>
/// <remarks>
/// For JPEG/PNG sources and outputs, pixels are kept as they are (no color conversion) and the pipeline writes
/// the metadata afterwards. For the other formats, orientation is applied and colors converted to sRGB, so the
/// result looks identical even though metadata cannot be carried over.
/// 16-bit images are refused in lossless mode because they would be reduced to 8 bits per channel.
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

    public bool CanResize => true;

    public static bool CanRead(MediaFormat format) => Readable.Contains(format);

    public bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options)
    {
        if (!Readable.Contains(input) || !Encoders.ContainsKey(output)) return false;

        // Re-encoding a JPEG/HEIC is never lossless: that is jpegtran's job.
        var sameFormat = input == output;
        if (sameFormat && options.Mode == CompressionMode.Lossless && MediaFormatInfo.IsLossyOnly(output))
            return false;

        // Re-encoding a BMP as BMP gains nothing.
        if (sameFormat && output is MediaFormat.Bmp)
            return false;

        return true;
    }

    public async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var options = job.Options;
        var keepPixels = MetadataTransfer.PipelineHandles(job.InputFormat, job.OutputFormat);
        var alphaMode = SupportsAlpha(job.OutputFormat) ? BitmapAlphaMode.Straight : BitmapAlphaMode.Ignore;

        var image = await WicCodec.DecodeAsync(
            job.InputPath,
            applyOrientation: !keepPixels || MetadataTransfer.BakesOrientation(job.InputFormat, job.OutputFormat),
            convertToSrgb: !keepPixels,
            alphaMode,
            cancellationToken,
            job.ResizeScale);

        if (image.FrameCount > 1)
            throw new NotSupportedException(L10n.T(
                "Images with several frames (animated GIF, multi-page TIFF) aren't supported yet.",
                "Les images à plusieurs images (GIF animé, TIFF multipage) ne sont pas encore prises en charge."));

        if (options.Mode == CompressionMode.Lossless && image.SourcePixelFormat is BitmapPixelFormat.Rgba16 or BitmapPixelFormat.Gray16)
            throw new NotSupportedException(L10n.T(
                "16-bit image: re-encoding it in 8 bits would not be lossless.",
                "Image 16 bits : la réencoder en 8 bits ne serait pas sans perte."));

        cancellationToken.ThrowIfCancellationRequested();

        await WicCodec.EncodeAsync(
            job.OutputPath,
            Encoders[job.OutputFormat],
            BuildEncoderOptions(job.OutputFormat, options),
            image,
            alphaMode,
            cancellationToken);
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
                var full = options.Tuning.ColorDetail switch
                {
                    ColorDetail.Full => true,
                    ColorDetail.Reduced => false,
                    _ => quality >= 0.9f,
                };
                set["JpegYCrCbSubsampling"] = new BitmapTypedValue((byte)(full ? 3 : 1), PropertyType.UInt8);
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
