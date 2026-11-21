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

    // Sound
    Mp3,

    /// <summary>AAC in an MP4 container (.m4a, .m4b).</summary>
    M4a,

    /// <summary>Raw AAC (ADTS, .aac): read only, written as <see cref="M4a"/>.</summary>
    Aac,

    Wav,
    Flac,

    /// <summary>Ogg Vorbis (.ogg, .oga).</summary>
    Ogg,

    /// <summary>Opus in Ogg (.opus, or an .ogg holding Opus).</summary>
    Opus,

    Aiff,
}

public enum MediaKind
{
    Unknown,
    Image,
    Video,
    Document,
    Audio,
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
        MediaFormat.Mp3 or MediaFormat.M4a or MediaFormat.Aac or MediaFormat.Wav or MediaFormat.Flac
            or MediaFormat.Ogg or MediaFormat.Opus or MediaFormat.Aiff => MediaKind.Audio,
        _ => MediaKind.Unknown,
    };

    /// <summary>Sound formats that keep every sample as it is (uncompressed or lossless).</summary>
    public static bool IsLosslessAudio(MediaFormat format) =>
        format is MediaFormat.Wav or MediaFormat.Aiff or MediaFormat.Flac;

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
        MediaFormat.Mp3 => ".mp3",
        MediaFormat.M4a => ".m4a",
        MediaFormat.Aac => ".aac",
        MediaFormat.Wav => ".wav",
        MediaFormat.Flac => ".flac",
        MediaFormat.Ogg => ".ogg",
        MediaFormat.Opus => ".opus",
        MediaFormat.Aiff => ".aiff",
        _ => string.Empty,
    };

    /// <summary>
    /// Output format when the user keeps the format: the input format, except for video containers
    /// Windows cannot write (MOV, MKV, AVI, WebM), which become MP4, uncompressed sound (WAV, AIFF), which becomes
    /// FLAC, and raw AAC, which goes into an M4A.
    /// </summary>
    public static MediaFormat DefaultOutputFor(MediaFormat input) => input switch
    {
        _ when KindOf(input) == MediaKind.Video => MediaFormat.Mp4,
        MediaFormat.Wav or MediaFormat.Aiff => MediaFormat.Flac,
        MediaFormat.Aac => MediaFormat.M4a,
        _ => input,
    };

    /// <summary>Output format for a job: the target if it is of the same kind as the input, else the default.</summary>
    public static MediaFormat ResolveOutput(MediaFormat input, MediaFormat? target) =>
        target is { } t && KindOf(t) == KindOf(input) ? (t == MediaFormat.Aac ? MediaFormat.M4a : t) : DefaultOutputFor(input);

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
        [".mp3"] = MediaFormat.Mp3,
        [".m4a"] = MediaFormat.M4a, [".m4b"] = MediaFormat.M4a,
        [".aac"] = MediaFormat.Aac,
        [".wav"] = MediaFormat.Wav,
        [".flac"] = MediaFormat.Flac,
        [".ogg"] = MediaFormat.Ogg, [".oga"] = MediaFormat.Ogg,
        [".opus"] = MediaFormat.Opus,
        [".aiff"] = MediaFormat.Aiff, [".aif"] = MediaFormat.Aiff, [".aifc"] = MediaFormat.Aiff,
    };

    /// <summary>Every extension Smash It recognizes, with the leading dot.</summary>
    public static IEnumerable<string> KnownExtensions => ByExtension.Keys;

    /// <summary>Guess from the extension only. Use <see cref="FormatDetector"/> for the real content.</summary>
    public static MediaFormat FromExtension(string path) =>
        ByExtension.TryGetValue(Path.GetExtension(path), out var format) ? format : MediaFormat.Unknown;

    public static bool TryParse(string value, out MediaFormat format)
    {
        var key = value.StartsWith('.') ? value : "." + value;
        return ByExtension.TryGetValue(key, out format);
    }
}
