using System.Text.Json;
using System.Text.Json.Serialization;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;

namespace SmashIt.App.Services;

/// <summary>Preferences kept between launches, in %LOCALAPPDATA%\SmashIt\settings.json.</summary>
public sealed class AppSettings
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SmashIt", "settings.json");

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    public static AppSettings Current { get; } = Load();

    public AppLanguage Language { get; set; } = AppLanguage.Automatic;

    // Compression settings, so a right-click from Explorer compresses the way the window was last set up.
    public bool ImageLossy { get; set; }
    public int ImageQuality { get; set; } = 85;
    /// <summary>Lossy images: long side, in pixels, they are scaled down to; 0 keeps the size.</summary>
    public int ImageMaxSide { get; set; }
    public MediaFormat? OutputFormat { get; set; }
    public CompressionEffort Effort { get; set; } = CompressionEffort.Balanced;
    public bool StripMetadata { get; set; } = true;
    public bool PdfLossy { get; set; }
    public int PdfQuality { get; set; } = 75;
    /// <summary>Lossy PDFs: images are scaled down to this resolution at most; 0 keeps them as they are.</summary>
    public int PdfMaxImageDpi { get; set; } = 150;
    public int VideoQuality { get; set; } = 70;
    /// <summary>Short side of the picture, in pixels, videos are scaled down to; 0 keeps the resolution.</summary>
    public int VideoMaxShortSide { get; set; }
    public VideoCodec VideoCodec { get; set; } = VideoCodec.H264;
    public bool KeepOriginal { get; set; } = true;
    public bool SaveInCompressedFolder { get; set; }
    public bool CopyAlreadyOptimal { get; set; }
    public bool AskOutputLocation { get; set; } = true;

    // Updates
    public bool CheckForUpdates { get; set; } = true;
    /// <summary>A version the user chose to skip: not offered again at startup (newer ones are).</summary>
    public string? SkippedVersion { get; set; }
    /// <summary>The version that ran last: a newer one at startup means it was just updated.</summary>
    public string? LastRunVersion { get; set; }

    private static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), Json) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // Unreadable settings: start from the defaults.
        }
        return new AppSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not saved: the choice still applies until the app closes.
        }
    }
}
