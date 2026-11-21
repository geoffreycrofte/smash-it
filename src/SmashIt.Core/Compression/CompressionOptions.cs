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

    /// <summary>
    /// Smallest at the same quality (30 to 60 % under H.264 in our tests), encoded by ffmpeg (SVT-AV1) on the CPU,
    /// a few times slower. Plays in current browsers, on recent phones and on Windows with the AV1 extension.
    /// </summary>
    Av1,
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

    /// <summary>
    /// With <see cref="KeptOriginalFolder"/>: files no engine could make smaller are copied there as they are,
    /// so the folder holds the whole set.
    /// </summary>
    public bool CopyAlreadyOptimal { get; init; }

    /// <summary>
    /// Lossy PDFs: images shown at more than this resolution (pixels per inch at their size on the page) are
    /// scaled down to it. 0 keeps every image's resolution.
    /// </summary>
    public int PdfMaxImageDpi { get; init; }

    /// <summary>
    /// Videos: the short side of the picture is brought down to this many pixels at most (1080 for 1080p, 720 for
    /// 720p), keeping the proportions. 0 keeps the resolution.
    /// </summary>
    public int VideoMaxShortSide { get; init; }

    /// <summary>
    /// Lossy images: the long side is brought down to this many pixels at most (2560, 1920…), keeping the
    /// proportions. 0 keeps the size. Never applied in lossless mode, which never changes pixels.
    /// </summary>
    public int ImageMaxSide { get; init; }

    /// <summary>Per-engine image settings (color detail, JPEG encoding, palette, dithering, Zopfli).</summary>
    public ImageTuning Tuning { get; init; } = ImageTuning.Default;

    public static CompressionOptions Default { get; } = new();
}
