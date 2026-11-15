using SmashIt.Core.Formats;

namespace SmashIt.Core.Compression;

public enum CompressionMode
{
    /// <summary>Pixels / samples must stay bit-identical.</summary>
    Lossless,

    /// <summary>Quality may be traded for size, driven by <see cref="CompressionOptions.Quality"/>.</summary>
    Lossy,
}

/// <summary>How much CPU time the engines may spend looking for a smaller file.</summary>
public enum CompressionEffort
{
    Fast,
    Balanced,
    Maximum,
}

/// <summary>What happens to the original file once a compressed version exists.</summary>
public enum OriginalHandling
{
    /// <summary>The original goes to the Recycle Bin, the compressed file takes its place (default).</summary>
    RecycleBin,

    /// <summary>The original is deleted for good.</summary>
    Overwrite,

    /// <summary>The original is untouched; the result is saved next to it as "name-compressed.ext".</summary>
    KeepAlongside,
}

public sealed record CompressionOptions
{
    public CompressionMode Mode { get; init; } = CompressionMode.Lossless;

    /// <summary>1–100, used in <see cref="CompressionMode.Lossy"/> mode only.</summary>
    public int Quality { get; init; } = 85;

    public CompressionEffort Effort { get; init; } = CompressionEffort.Balanced;

    /// <summary>Output format; <c>null</c> keeps the input format.</summary>
    public MediaFormat? TargetFormat { get; init; }

    /// <summary>
    /// Removes EXIF, XMP, IPTC and comments. The color profile and the orientation are always kept,
    /// since dropping them would change how the image looks.
    /// </summary>
    public bool StripMetadata { get; init; } = true;

    public OriginalHandling OriginalHandling { get; init; } = OriginalHandling.RecycleBin;

    /// <summary>Suffix added before the extension when <see cref="OriginalHandling.KeepAlongside"/> is used.</summary>
    public string KeptOriginalSuffix { get; init; } = "-compressed";

    public static CompressionOptions Default { get; } = new();
}
