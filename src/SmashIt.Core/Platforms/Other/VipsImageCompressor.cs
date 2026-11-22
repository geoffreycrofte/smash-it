using NetVips;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Imaging;
using SmashIt.Core.Metadata;

namespace SmashIt.Core.Compressors;

/// <summary>
/// Re-encodes images with libvips, on macOS and Linux: WIC's counterpart (<see cref="VipsImageCodec"/> reads them).
/// Writes JPEG, PNG and TIFF; WebP and AVIF are cwebp's and avifenc's job, and HEIC, BMP and JPEG XR stay Windows-only.
/// </summary>
/// <remarks>
/// Same rules as the WIC engine: for JPEG/PNG sources and outputs, pixels are kept as they are (no color conversion)
/// and the pipeline writes the metadata afterwards; for the other formats, orientation is applied and colors
/// converted to sRGB. 16-bit images are refused in lossless mode. Transparent pixels written to a JPEG are
/// laid on white.
/// </remarks>
internal sealed class VipsImageCompressor(VipsImageCodec codec) : ICompressor
{
    private static readonly HashSet<MediaFormat> Writable = [MediaFormat.Jpeg, MediaFormat.Png, MediaFormat.Tiff];

    public string Id => "vips";

    public string DisplayName => "libvips";

    public bool CanResize => true;

    public bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options)
    {
        if (!codec.CanRead(input) || !Writable.Contains(output)) return false;

        // Re-encoding a JPEG is never lossless: that is jpegtran's job.
        return !(input == output && options.Mode == CompressionMode.Lossless && MediaFormatInfo.IsLossyOnly(output));
    }

    public async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var options = job.Options;
        var keepPixels = MetadataTransfer.PipelineHandles(job.InputFormat, job.OutputFormat);

        using var decoded = await codec.OpenAsync(job.InputPath, cancellationToken);
        if (VipsImageCodec.PageCount(decoded.Image) > 1)
            throw new NotSupportedException(L10n.T(
                "Images with several frames (animated GIF, multi-page TIFF) aren't supported yet.",
                "Les images à plusieurs images (GIF animé, TIFF multipage) ne sont pas encore prises en charge."));

        if (options.Mode == CompressionMode.Lossless && decoded.Image.Format is Enums.BandFormat.Ushort)
            throw new NotSupportedException(L10n.T(
                "16-bit image: re-encoding it in 8 bits would not be lossless.",
                "Image 16 bits : la réencoder en 8 bits ne serait pas sans perte."));

        await Task.Run(() =>
        {
            var conversion = new PngConversion(
                ApplyOrientation: !keepPixels || MetadataTransfer.BakesOrientation(job.InputFormat, job.OutputFormat),
                ConvertToSrgb: !keepPixels,
                job.ResizeScale);
            using var prepared = VipsImageCodec.Prepare(decoded.Image, conversion);
            cancellationToken.ThrowIfCancellationRequested();
            Encode(prepared, job.OutputPath, job.OutputFormat, options);
        }, cancellationToken);
    }

    private static void Encode(Image image, string path, MediaFormat format, CompressionOptions options)
    {
        var quality = Math.Clamp(options.Quality, 1, 100);
        switch (format)
        {
            case MediaFormat.Jpeg:
                // 4:4:4 keeps sharp colors, 4:2:0 is smaller: the same automatic choice as the WIC engine.
                var full = options.Tuning.ColorDetail switch
                {
                    ColorDetail.Full => true,
                    ColorDetail.Reduced => false,
                    _ => quality >= 90,
                };
                using (var opaque = image.HasAlpha() ? image.Flatten(background: [255, 255, 255]) : image.Copy())
                {
                    opaque.Jpegsave(
                        path,
                        q: quality,
                        optimizeCoding: true,
                        subsampleMode: full ? Enums.ForeignSubsample.Off : Enums.ForeignSubsample.On,
                        keep: Enums.ForeignKeep.None);
                }
                break;
            case MediaFormat.Png:
                // Adaptive filters; oxipng and pngquant work on it afterwards.
                image.Pngsave(path, compression: 6, filter: Enums.ForeignPngFilter.All, keep: Enums.ForeignKeep.None);
                break;
            case MediaFormat.Tiff:
                image.Tiffsave(path, compression: Enums.ForeignTiffCompression.Deflate, keep: Enums.ForeignKeep.None);
                break;
            default:
                throw new NotSupportedException(format.ToString());
        }
    }
}
