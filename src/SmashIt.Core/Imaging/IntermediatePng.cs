using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Metadata;
using Windows.Graphics.Imaging;

namespace SmashIt.Core.Imaging;

/// <summary>
/// Turns any image Windows can read (HEIC, TIFF, WebP…) into a PNG that external encoders (cwebp, avifenc)
/// understand. Orientation is baked into the pixels; the color profile and, if asked, EXIF/XMP are embedded
/// so the encoder can carry them over.
/// </summary>
internal static class IntermediatePng
{
    public static async Task CreateAsync(CompressionJob job, string pngPath, CancellationToken cancellationToken)
    {
        var metadata = MetadataTransfer.Read(job.InputPath, job.InputFormat);

        // Without a readable profile we cannot embed it, so convert colors to sRGB instead.
        var image = await WicCodec.DecodeAsync(
            job.InputPath,
            applyOrientation: true,
            convertToSrgb: metadata is null,
            BitmapAlphaMode.Straight,
            cancellationToken);

        if (image.FrameCount > 1)
            throw new NotSupportedException("Les images animées ou multipages ne sont pas encore prises en charge.");

        var options = new BitmapPropertySet
        {
            // WICPngFilterNone: this file is temporary, encoding speed matters more than size.
            ["FilterOption"] = new BitmapTypedValue((byte)1, Windows.Foundation.PropertyType.UInt8),
        };
        await WicCodec.EncodeAsync(pngPath, BitmapEncoder.PngEncoderId, options, image, BitmapAlphaMode.Straight, cancellationToken);

        if (metadata is not null)
        {
            var selected = MetadataTransfer.Select(metadata, job.Options, orientationBaked: true, MediaFormat.Png);
            MetadataTransfer.Write(pngPath, MediaFormat.Png, selected);
        }
    }
}
