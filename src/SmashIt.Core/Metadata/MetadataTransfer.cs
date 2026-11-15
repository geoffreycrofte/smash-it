using SmashIt.Core.Compression;
using SmashIt.Core.Formats;

namespace SmashIt.Core.Metadata;

/// <summary>
/// Decides which metadata survives a compression and writes it into the result.
/// </summary>
/// <remarks>
/// Rules:
/// <list type="bullet">
/// <item>The ICC color profile is always kept: removing it would change the colors.</item>
/// <item>The orientation is never lost: either the pixels are rotated ("baked"), or the EXIF orientation is kept.</item>
/// <item>EXIF, XMP, IPTC, comments and text chunks are kept only when <see cref="CompressionOptions.StripMetadata"/> is off.</item>
/// </list>
/// </remarks>
public static class MetadataTransfer
{
    public static bool CanRead(MediaFormat format) => format is MediaFormat.Jpeg or MediaFormat.Png;

    public static bool CanWrite(MediaFormat format) => format is MediaFormat.Jpeg or MediaFormat.Png;

    /// <summary>True when the pipeline rewrites the output's metadata itself (JPEG/PNG → JPEG/PNG).</summary>
    public static bool PipelineHandles(MediaFormat input, MediaFormat output) => CanRead(input) && CanWrite(output);

    /// <summary>True when Smash It can carry EXIF/XMP over for this conversion.</summary>
    public static bool CanPreserve(MediaFormat input, MediaFormat output) =>
        CanRead(input) && (CanWrite(output) || output is MediaFormat.WebP or MediaFormat.Avif);

    /// <summary>
    /// When converting, pixels are physically rotated and the orientation tag reset to 1, because not every
    /// viewer honors EXIF orientation in PNG/WebP. Same-format jobs keep the pixels and the tag as they are.
    /// </summary>
    public static bool BakesOrientation(MediaFormat input, MediaFormat output) => input != output;

    /// <summary>Reads the metadata of a JPEG/PNG; <c>null</c> for other formats or unreadable files.</summary>
    public static ImageMetadata? Read(string path, MediaFormat format)
    {
        if (!CanRead(format)) return null;
        try
        {
            var bytes = File.ReadAllBytes(path);
            return format == MediaFormat.Jpeg ? JpegMetadataIO.Read(bytes) : PngMetadataIO.Read(bytes);
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    /// <summary>Picks what to write into the output, following the rules above.</summary>
    public static ImageMetadata Select(
        ImageMetadata source,
        CompressionOptions options,
        bool orientationBaked,
        MediaFormat outputFormat)
    {
        var orientation = source.Exif is null ? 1 : ExifOrientation.Read(source.Exif) ?? 1;

        byte[]? exif;
        if (options.StripMetadata)
            exif = !orientationBaked && orientation != 1 ? ExifOrientation.CreateMinimal(orientation) : null;
        else if (source.Exif is null)
            exif = null;
        else
            exif = orientationBaked && orientation != 1 ? ExifOrientation.WithOrientation(source.Exif, 1) : source.Exif;

        return new ImageMetadata
        {
            SourceFormat = source.SourceFormat,
            Icc = source.Icc,
            Exif = exif,
            Xmp = options.StripMetadata ? null : source.Xmp,
            Extras = !options.StripMetadata && outputFormat == source.SourceFormat ? source.Extras : [],
        };
    }

    /// <summary>Replaces the metadata of a JPEG/PNG file in place.</summary>
    public static void Write(string path, MediaFormat format, ImageMetadata metadata)
    {
        var bytes = File.ReadAllBytes(path);
        var result = format switch
        {
            MediaFormat.Jpeg => JpegMetadataIO.Write(bytes, metadata),
            MediaFormat.Png => PngMetadataIO.Write(bytes, metadata),
            _ => throw new NotSupportedException($"Écriture des métadonnées impossible en {format}."),
        };
        File.WriteAllBytes(path, result);
    }
}
