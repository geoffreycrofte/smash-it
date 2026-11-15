using SmashIt.Core.Formats;

namespace SmashIt.Core.Metadata;

/// <summary>
/// A container-specific block copied verbatim (JPEG segment marker as "FFED", PNG chunk type as "tEXt"…).
/// Only written back into a file of the same container.
/// </summary>
public sealed record RawMetadataBlock(string Type, byte[] Data);

/// <summary>Metadata extracted from a JPEG or PNG, in container-neutral form where possible.</summary>
public sealed record ImageMetadata
{
    public required MediaFormat SourceFormat { get; init; }

    /// <summary>EXIF as a TIFF structure (without the JPEG "Exif\0\0" prefix).</summary>
    public byte[]? Exif { get; init; }

    /// <summary>ICC color profile.</summary>
    public byte[]? Icc { get; init; }

    /// <summary>XMP packet (UTF-8 XML).</summary>
    public byte[]? Xmp { get; init; }

    /// <summary>IPTC, comments, text chunks… copied only within the same container.</summary>
    public IReadOnlyList<RawMetadataBlock> Extras { get; init; } = [];

    public bool IsEmpty => Exif is null && Icc is null && Xmp is null && Extras.Count == 0;
}
