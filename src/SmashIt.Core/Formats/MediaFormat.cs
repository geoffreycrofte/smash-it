namespace SmashIt.Core.Formats;

public enum MediaFormat
{
    Unknown,

    // Images
    Jpeg,
    Png,
    Gif,
    Bmp,
    Tiff,
    WebP,
    Heif,
    Avif,
    JpegXr,
    Svg,

    // Documents
    Pdf,

    // Videos
    Mp4,
    Mov,
    Mkv,
    WebM,
    Avi,
}

public enum MediaKind
{
    Unknown,
    Image,
    Video,
    Document,
}

public static class MediaFormatInfo
{
    public static MediaKind KindOf(MediaFormat format) => format switch
    {
        MediaFormat.Jpeg or MediaFormat.Png or MediaFormat.Gif or MediaFormat.Bmp or MediaFormat.Tiff
            or MediaFormat.WebP or MediaFormat.Heif or MediaFormat.Avif or MediaFormat.JpegXr
            or MediaFormat.Svg => MediaKind.Image,
        MediaFormat.Pdf => MediaKind.Document,
        MediaFormat.Mp4 or MediaFormat.Mov or MediaFormat.Mkv or MediaFormat.WebM
            or MediaFormat.Avi => MediaKind.Video,
        _ => MediaKind.Unknown,
    };

    /// <summary>Preferred file extension (with leading dot) used when writing this format.</summary>
    public static string ExtensionOf(MediaFormat format) => format switch
    {
        MediaFormat.Jpeg => ".jpg",
        MediaFormat.Png => ".png",
        MediaFormat.Gif => ".gif",
        MediaFormat.Bmp => ".bmp",
        MediaFormat.Tiff => ".tif",
        MediaFormat.WebP => ".webp",
        MediaFormat.Heif => ".heic",
        MediaFormat.Avif => ".avif",
        MediaFormat.JpegXr => ".jxr",
        MediaFormat.Svg => ".svg",
        MediaFormat.Pdf => ".pdf",
        MediaFormat.Mp4 => ".mp4",
        MediaFormat.Mov => ".mov",
        MediaFormat.Mkv => ".mkv",
        MediaFormat.WebM => ".webm",
        MediaFormat.Avi => ".avi",
        _ => string.Empty,
    };

    /// <summary>
    /// Output format when the user keeps the format: the input format, except for video containers
    /// Windows cannot write (MOV, MKV, AVI, WebM), which become MP4.
    /// </summary>
    public static MediaFormat DefaultOutputFor(MediaFormat input) =>
        KindOf(input) == MediaKind.Video ? MediaFormat.Mp4 : input;

    /// <summary>Output format for a job: the target if it is of the same kind as the input, else the default.</summary>
    public static MediaFormat ResolveOutput(MediaFormat input, MediaFormat? target) =>
        target is { } t && KindOf(t) == KindOf(input) ? t : DefaultOutputFor(input);

    /// <summary>Formats whose encoding is always lossy: re-encoding them in lossless mode is not allowed.</summary>
    public static bool IsLossyOnly(MediaFormat format) =>
        format is MediaFormat.Jpeg or MediaFormat.Heif;

    private static readonly Dictionary<string, MediaFormat> ByExtension = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = MediaFormat.Jpeg, [".jpeg"] = MediaFormat.Jpeg, [".jpe"] = MediaFormat.Jpeg, [".jfif"] = MediaFormat.Jpeg,
        [".png"] = MediaFormat.Png,
        [".gif"] = MediaFormat.Gif,
        [".bmp"] = MediaFormat.Bmp, [".dib"] = MediaFormat.Bmp,
        [".tif"] = MediaFormat.Tiff, [".tiff"] = MediaFormat.Tiff,
        [".webp"] = MediaFormat.WebP,
        [".heic"] = MediaFormat.Heif, [".heif"] = MediaFormat.Heif, [".hif"] = MediaFormat.Heif,
        [".avif"] = MediaFormat.Avif,
        [".jxr"] = MediaFormat.JpegXr, [".wdp"] = MediaFormat.JpegXr, [".hdp"] = MediaFormat.JpegXr,
        [".svg"] = MediaFormat.Svg,
        [".pdf"] = MediaFormat.Pdf,
        [".mp4"] = MediaFormat.Mp4, [".m4v"] = MediaFormat.Mp4,
        [".mov"] = MediaFormat.Mov,
        [".mkv"] = MediaFormat.Mkv,
        [".webm"] = MediaFormat.WebM,
        [".avi"] = MediaFormat.Avi,
    };

    /// <summary>Guess from the extension only. Use <see cref="FormatDetector"/> for the real content.</summary>
    public static MediaFormat FromExtension(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var format) ? format : MediaFormat.Unknown;

    public static bool TryParse(string value, out MediaFormat format)
    {
        var key = value.StartsWith('.') ? value : "." + value;
        return ByExtension.TryGetValue(key, out format);
    }
}
