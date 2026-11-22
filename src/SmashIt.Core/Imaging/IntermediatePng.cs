using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Metadata;

namespace SmashIt.Core.Imaging;

/// <summary>
/// Turns any image the system can read (HEIC, TIFF, WebP…) into a PNG that external encoders (cwebp, avifenc)
/// understand. Orientation is baked into the pixels; the color profile and, if asked, EXIF/XMP are embedded
/// so the encoder can carry them over.
/// </summary>
internal static class IntermediatePng
{
    public static async Task CreateAsync(CompressionJob job, string pngPath, CancellationToken cancellationToken)
    {
        var metadata = MetadataTransfer.Read(job.InputPath, job.InputFormat);

        // Without a readable profile we cannot embed it, so convert colors to sRGB instead.
        var frames = await Platform.ImageCodec.WritePngAsync(
            job.InputPath,
            pngPath,
            new PngConversion(ApplyOrientation: true, ConvertToSrgb: metadata is null, job.ResizeScale, Fast: true),
            cancellationToken);

        if (frames > 1)
            throw new NotSupportedException(L10n.T(
                "Animated and multi-page images aren't supported yet.",
                "Les images animées ou multipages ne sont pas encore prises en charge."));

        // HEIC/AVIF/WebP sources: their pixels are now sRGB, but EXIF and XMP still describe the picture.
        metadata ??= job.Options.StripMetadata ? null : MetadataTransfer.ReadDescriptive(job.InputPath, job.InputFormat);
        if (metadata is not null)
        {
            var selected = MetadataTransfer.Select(metadata, job.Options, orientationBaked: true, MediaFormat.Png);
            MetadataTransfer.Write(pngPath, MediaFormat.Png, selected);
        }
    }
}
