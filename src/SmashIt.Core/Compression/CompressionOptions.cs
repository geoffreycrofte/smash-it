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

public enum VideoCodec
{
    /// <summary>Plays everywhere.</summary>
    H264,

    /// <summary>About 40 % smaller at the same quality; needs a HEVC-capable player.</summary>
    Hevc,
}

public sealed record CompressionOptions
{
    public CompressionMode Mode { get; init; } = CompressionMode.Lossless;

    /// <summary>1–100, used in <see cref="CompressionMode.Lossy"/> mode only.</summary>
    public int Quality { get; init; } = 85;

    public CompressionEffort Effort { get; init; } = CompressionEffort.Balanced;

    /// <summary>
    /// Output format; <c>null</c> keeps the input format. Ignored for files of another kind
    /// (an image target does not apply to PDFs or videos).
    /// </summary>
    public MediaFormat? TargetFormat { get; init; }

    public VideoCodec VideoCodec { get; init; } = VideoCodec.H264;

    /// <summary>
    /// Removes EXIF, XMP, IPTC and comments. The color profile and the orientation are always kept,
    /// since dropping them would change how the image looks.
    /// </summary>
    public bool StripMetadata { get; init; } = true;

    public OriginalHandling OriginalHandling { get; init; } = OriginalHandling.RecycleBin;

    /// <summary>Suffix added before the extension when <see cref="OriginalHandling.KeepAlongside"/> is used.</summary>
    public string KeptOriginalSuffix { get; init; } = "-compressed";

    /// <summary>Folder name used by <see cref="KeptOriginalFolder"/> in the app and the command line.</summary>
    public const string CompressedFolderName = "compressed";

    /// <summary>
    /// With <see cref="OriginalHandling.KeepAlongside"/>: when set, results go into this subfolder of the
    /// original's folder (created if needed), under the original name instead of "name-compressed.ext".
    /// </summary>
    public string? KeptOriginalFolder { get; init; }

    public static CompressionOptions Default { get; } = new();
}
