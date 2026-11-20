namespace SmashIt.Core.Compression;

public enum PresetId
{
    /// <summary>Lossless everywhere: the default, nothing visible changes.</summary>
    Lossless,

    /// <summary>For websites: lossy, light, metadata removed.</summary>
    Web,

    /// <summary>The lightest, for attachments.</summary>
    Email,

    /// <summary>Lossless and metadata kept, for keeping files as they were.</summary>
    Archive,
}

/// <summary>The settings a preset sets, for each kind of file. Video is always lossy.</summary>
public sealed record PresetSettings(
    bool ImageLossy,
    int ImageQuality,
    bool StripMetadata,
    bool PdfLossy,
    int PdfQuality,
    int PdfMaxImageDpi,
    int VideoQuality,
    int VideoMaxShortSide = 0,
    int ImageMaxSide = 0);

/// <summary>Ready-made settings: one choice instead of a setting per kind of file.</summary>
public static class CompressionPresets
{
    public static IReadOnlyDictionary<PresetId, PresetSettings> All { get; } = new Dictionary<PresetId, PresetSettings>
    {
        [PresetId.Lossless] = new(ImageLossy: false, ImageQuality: 85, StripMetadata: true,
            PdfLossy: false, PdfQuality: 75, PdfMaxImageDpi: 150, VideoQuality: 70),
        // Web: 1080p videos and 2560 px images are plenty for a page, even on a large screen. Email: 720p and
        // 1920 px keep phone videos and photos small enough to send.
        [PresetId.Web] = new(ImageLossy: true, ImageQuality: 80, StripMetadata: true,
            PdfLossy: true, PdfQuality: 75, PdfMaxImageDpi: 150, VideoQuality: 65, VideoMaxShortSide: 1080, ImageMaxSide: 2560),
        [PresetId.Email] = new(ImageLossy: true, ImageQuality: 70, StripMetadata: true,
            PdfLossy: true, PdfQuality: 60, PdfMaxImageDpi: 150, VideoQuality: 55, VideoMaxShortSide: 720, ImageMaxSide: 1920),
        [PresetId.Archive] = new(ImageLossy: false, ImageQuality: 90, StripMetadata: false,
            PdfLossy: false, PdfQuality: 90, PdfMaxImageDpi: 300, VideoQuality: 80),
    };

    /// <summary>
    /// The preset these settings amount to, or <c>null</c> for custom ones. Settings that have no effect are
    /// ignored: a quality or an image size limit in lossless mode, the PDF resolution when PDFs are lossless.
    /// </summary>
    public static PresetId? Match(PresetSettings settings) =>
        All.Where(p => Same(p.Value, settings)).Select(p => (PresetId?)p.Key).FirstOrDefault();

    private static bool Same(PresetSettings preset, PresetSettings current) =>
        preset.ImageLossy == current.ImageLossy &&
        (!preset.ImageLossy || (preset.ImageQuality == current.ImageQuality && preset.ImageMaxSide == current.ImageMaxSide)) &&
        preset.StripMetadata == current.StripMetadata &&
        preset.PdfLossy == current.PdfLossy &&
        (!preset.PdfLossy || (preset.PdfQuality == current.PdfQuality && preset.PdfMaxImageDpi == current.PdfMaxImageDpi)) &&
        preset.VideoQuality == current.VideoQuality &&
        preset.VideoMaxShortSide == current.VideoMaxShortSide;
}
