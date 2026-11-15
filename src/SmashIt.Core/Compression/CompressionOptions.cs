using SmashIt.Core.Formats;

namespace SmashIt.Core.Compression;

public enum CompressionMode
{
    /// <summary>Pixels / samples must stay bit-identical.</summary>
    Lossless,

    /// <summary>Quality may be traded for size, driven by <see cref="CompressionOptions.Quality"/>.</summary>
    Lossy,
}

/// <summary>What happens to the original file when a smaller version replaces it.</summary>
public enum OriginalHandling
{
    /// <summary>The original goes to the Recycle Bin, the optimized file takes its place (default).</summary>
    RecycleBin,

    /// <summary>The original is overwritten for good.</summary>
    Overwrite,

    /// <summary>The original is untouched; the result is saved next to it with a suffix.</summary>
    KeepAlongside,
}

public sealed record CompressionOptions
{
    public CompressionMode Mode { get; init; } = CompressionMode.Lossless;

    /// <summary>1–100, used in <see cref="CompressionMode.Lossy"/> mode only.</summary>
    public int Quality { get; init; } = 85;

    /// <summary>Output format; <c>null</c> keeps the input format.</summary>
    public MediaFormat? TargetFormat { get; init; }

    public bool StripMetadata { get; init; } = true;

    public OriginalHandling OriginalHandling { get; init; } = OriginalHandling.RecycleBin;

    /// <summary>Suffix added before the extension when <see cref="OriginalHandling.KeepAlongside"/> is used.</summary>
    public string AlongsideSuffix { get; init; } = ".smashed";

    public static CompressionOptions Default { get; } = new();
}
