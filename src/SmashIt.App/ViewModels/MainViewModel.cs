using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmashIt.App.Services;
using SmashIt.Core;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.IO;
using SmashIt.Core.Shell;

namespace SmashIt.App.ViewModels;

// Labels are read when a window binds them, so they follow the current language.

public sealed record OutputFormatChoice(string? Name, MediaFormat? Format)
{
    public string Label => Name ?? L10n.T("Keep the format", "Conserver le format");

    public override string ToString() => Label;
}

public sealed record EffortChoice(CompressionEffort Effort)
{
    public string Label => Effort switch
    {
        CompressionEffort.Fast => L10n.T("Fast", "Rapide"),
        CompressionEffort.Maximum => L10n.T("Maximum", "Maximum"),
        _ => L10n.T("Balanced", "Équilibré"),
    };

    public override string ToString() => Label;
}

public sealed record VideoCodecChoice(VideoCodec Codec)
{
    public string Label => Codec == VideoCodec.Hevc
        ? L10n.T("HEVC / H.265 (smaller)", "HEVC / H.265 (plus léger)")
        : L10n.T("H.264 (plays everywhere)", "H.264 (compatible partout)");

    public override string ToString() => Label;
}

public sealed record LanguageChoice(AppLanguage Language)
{
    public string Label => Language switch
    {
        AppLanguage.English => "English",
        AppLanguage.French => "Français",
        _ => L10n.T("Automatic (Windows language)", "Automatique (langue de Windows)"),
    };

    public override string ToString() => Label;
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly CompressionPipeline _pipeline = SmashItEngine.CreateDefaultPipeline();
    private readonly SemaphoreSlim _slots = new(Math.Max(1, Environment.ProcessorCount - 1));

    // GPU encoders accept few simultaneous sessions, and one video already keeps the machine busy.
    private readonly SemaphoreSlim _videoSlots = new(1);
    private CancellationTokenSource _cts = new();

    public ObservableCollection<FileItem> Files { get; } = [];

    public IReadOnlyList<OutputFormatChoice> OutputFormats { get; } =
    [
        new(null, null),
        new("JPEG", MediaFormat.Jpeg),
        new("PNG", MediaFormat.Png),
        new("WebP", MediaFormat.WebP),
        new("AVIF", MediaFormat.Avif),
        new("HEIC", MediaFormat.Heif),
        new("TIFF", MediaFormat.Tiff),
        new("JPEG XR", MediaFormat.JpegXr),
        new("BMP", MediaFormat.Bmp),
    ];

    public IReadOnlyList<EffortChoice> Efforts { get; } =
    [
        new(CompressionEffort.Fast),
        new(CompressionEffort.Balanced),
        new(CompressionEffort.Maximum),
    ];

    public IReadOnlyList<VideoCodecChoice> VideoCodecs { get; } =
    [
        new(VideoCodec.H264),
        new(VideoCodec.Hevc),
    ];

    public IReadOnlyList<LanguageChoice> Languages { get; } =
    [
        new(AppLanguage.Automatic),
        new(AppLanguage.English),
        new(AppLanguage.French),
    ];

    /// <summary>Raised after the language changed: the windows must be rebuilt to show the new texts.</summary>
    public event Action? LanguageChanged;

    [ObservableProperty]
    public partial LanguageChoice SelectedLanguage { get; set; }

    [ObservableProperty]
    public partial bool IsSettingsOpen { get; set; }

    // Images
    [ObservableProperty]
    public partial bool ImageLossy { get; set; }

    [ObservableProperty]
    public partial double ImageQuality { get; set; } = 85;

    [ObservableProperty]
    public partial OutputFormatChoice SelectedOutputFormat { get; set; }

    [ObservableProperty]
    public partial EffortChoice SelectedEffort { get; set; }

    [ObservableProperty]
    public partial bool StripMetadata { get; set; } = true;

    // PDF
    [ObservableProperty]
    public partial bool PdfLossy { get; set; }

    [ObservableProperty]
    public partial double PdfQuality { get; set; } = 75;

    // Video (always lossy)
    [ObservableProperty]
    public partial double VideoQuality { get; set; } = 70;

    [ObservableProperty]
    public partial VideoCodecChoice SelectedVideoCodec { get; set; }

    // All files
    // On by default: nothing is replaced until the user asks for it.
    [ObservableProperty]
    public partial bool KeepOriginal { get; set; } = true;

    // Explorer: the registry is the source of truth, not the settings file.
    private readonly ExplorerContextMenu _contextMenu = ExplorerContextMenu.ForCurrentUser();

    [ObservableProperty]
    public partial bool ContextMenuEnabled { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    [NotifyPropertyChangedFor(nameof(HasFiles))]
    public partial int FileCount { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = string.Empty;

    /// <summary>True while files are waiting or being compressed: the footer shows a spinner.</summary>
    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    public bool IsEmpty => FileCount == 0;

    public bool HasFiles => FileCount > 0;

    /// <summary>Properties saved in <see cref="AppSettings"/> when they change.</summary>
    private static readonly HashSet<string> SavedProperties =
    [
        nameof(ImageLossy), nameof(ImageQuality), nameof(SelectedOutputFormat), nameof(SelectedEffort),
        nameof(StripMetadata), nameof(PdfLossy), nameof(PdfQuality), nameof(VideoQuality),
        nameof(SelectedVideoCodec), nameof(KeepOriginal),
    ];

    // Sliders change many times per second: the file is written once they settle.
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveTimer;

    public MainViewModel()
    {
        var settings = AppSettings.Current;
        ImageLossy = settings.ImageLossy;
        ImageQuality = Math.Clamp(settings.ImageQuality, 1, 100);
        SelectedOutputFormat = OutputFormats.FirstOrDefault(f => f.Format == settings.OutputFormat) ?? OutputFormats[0];
        SelectedEffort = Efforts.FirstOrDefault(e => e.Effort == settings.Effort) ?? Efforts[1];
        StripMetadata = settings.StripMetadata;
        PdfLossy = settings.PdfLossy;
        PdfQuality = Math.Clamp(settings.PdfQuality, 1, 100);
        VideoQuality = Math.Clamp(settings.VideoQuality, 1, 100);
        SelectedVideoCodec = VideoCodecs.FirstOrDefault(c => c.Codec == settings.VideoCodec) ?? VideoCodecs[0];
        KeepOriginal = settings.KeepOriginal;
        SelectedLanguage = Languages.First(l => l.Language == settings.Language);

        // The entry points to a program that is gone (portable folder moved, old install): point it here.
        if (_contextMenu.RegisteredExecutable is { } registered && !File.Exists(registered)) UpdateContextMenu(true);
        ContextMenuEnabled = _contextMenu.IsRegistered;

        if (Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread() is { } dispatcher)
        {
            _saveTimer = dispatcher.CreateTimer();
            _saveTimer.Interval = TimeSpan.FromMilliseconds(500);
            _saveTimer.IsRepeating = false;
            _saveTimer.Tick += (_, _) => SaveSettings();
        }
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { } name && SavedProperties.Contains(name))
            {
                if (_saveTimer is null) SaveSettings();
                else { _saveTimer.Stop(); _saveTimer.Start(); }
            }
        };

        Files.CollectionChanged += (_, _) => FileCount = Files.Count;
        UpdateSummary();
    }

    private void SaveSettings()
    {
        var settings = AppSettings.Current;
        settings.ImageLossy = ImageLossy;
        settings.ImageQuality = (int)Math.Round(ImageQuality);
        settings.OutputFormat = SelectedOutputFormat.Format;
        settings.Effort = SelectedEffort.Effort;
        settings.StripMetadata = StripMetadata;
        settings.PdfLossy = PdfLossy;
        settings.PdfQuality = (int)Math.Round(PdfQuality);
        settings.VideoQuality = (int)Math.Round(VideoQuality);
        settings.VideoCodec = SelectedVideoCodec.Codec;
        settings.KeepOriginal = KeepOriginal;
        settings.Save();
    }

    /// <summary>Writes a pending change right away (the window is closing).</summary>
    public void FlushSettings()
    {
        if (_saveTimer is { IsRunning: true })
        {
            _saveTimer.Stop();
            SaveSettings();
        }
    }

    partial void OnSelectedLanguageChanged(LanguageChoice value)
    {
        if (value is null || value.Language == AppSettings.Current.Language) return;
        AppSettings.Current.Language = value.Language;
        AppSettings.Current.Save();
        L10n.Use(value.Language);
        // The menu label follows the language, when the entry is this program's.
        if (_contextMenu.IsRegisteredFor(Environment.ProcessPath!)) UpdateContextMenu(true);
        UpdateSummary();
        LanguageChanged?.Invoke();
    }

    partial void OnContextMenuEnabledChanged(bool value)
    {
        if (value != _contextMenu.IsRegistered) UpdateContextMenu(value);
    }

    private void UpdateContextMenu(bool enabled)
    {
        try
        {
            if (enabled) _contextMenu.Register(Environment.ProcessPath!, Strings.ContextMenuLabel);
            else _contextMenu.Unregister();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            // Registry locked by a policy: the switch shows what is really there.
        }
        ContextMenuEnabled = _contextMenu.IsRegistered;
    }

    /// <summary>The settings of the section matching the kind of file (images, PDF, video).</summary>
    private CompressionOptions OptionsFor(MediaKind kind)
    {
        var common = new CompressionOptions
        {
            OriginalHandling = KeepOriginal ? OriginalHandling.KeepAlongside : OriginalHandling.RecycleBin,
        };
        return kind switch
        {
            MediaKind.Document => common with
            {
                Mode = PdfLossy ? CompressionMode.Lossy : CompressionMode.Lossless,
                Quality = (int)Math.Round(PdfQuality),
            },
            MediaKind.Video => common with
            {
                Mode = CompressionMode.Lossy,
                Quality = (int)Math.Round(VideoQuality),
                VideoCodec = SelectedVideoCodec.Codec,
            },
            _ => common with
            {
                Mode = ImageLossy ? CompressionMode.Lossy : CompressionMode.Lossless,
                Quality = (int)Math.Round(ImageQuality),
                Effort = SelectedEffort.Effort,
                TargetFormat = SelectedOutputFormat.Format,
                StripMetadata = StripMetadata,
            },
        };
    }

    /// <summary>Adds files and folders and starts processing them with the current settings.</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        foreach (var path in InputCollector.Expand(paths))
        {
            var item = new FileItem(path);
            Files.Add(item);
            item.ThumbnailLoading = Thumbnails.LoadAsync(item);
            _ = ProcessAsync(item, OptionsFor(item.Kind), _cts.Token);
        }
    }

    [RelayCommand]
    private void Reprocess()
    {
        foreach (var item in Files.Where(f => f.State is not FileItemState.Working and not FileItemState.Pending).ToList())
        {
            // A converted file stays where it is; re-run on the original.
            item.Result = null;
            _ = ProcessAsync(item, OptionsFor(item.Kind), _cts.Token);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        foreach (var item in Files) PreviewCache.Forget(item.PreviewOriginalPath);
        Files.Clear();
        UpdateSummary();
    }

    /// <summary>Runs on the UI thread; the heavy work is pushed to the thread pool.</summary>
    private async Task ProcessAsync(FileItem item, CompressionOptions options, CancellationToken ct)
    {
        item.State = FileItemState.Pending;
        item.Progress = 0;
        UpdateSummary();
        var slots = item.Kind == MediaKind.Video ? _videoSlots : _slots;
        try
        {
            await slots.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            item.State = FileItemState.Working;
            UpdateSummary();

            // The thumbnail must be read before the original may go to the Recycle Bin.
            if (item.ThumbnailLoading is { } thumbnail) await thumbnail;

            // Keep the first "before" (later runs would start from the already compressed file).
            if (item.Kind == MediaKind.Image && item.PreviewOriginalPath is null)
                item.PreviewOriginalPath = await Task.Run(() => PreviewCache.Keep(item.Path), ct);

            // Created on the UI thread: reports come back on it.
            var progress = new Progress<double>(value => item.Progress = value);
            var result = await Task.Run(() => _pipeline.ProcessAsync(item.Path, options, ct, progress), ct);
            item.Result = result;
            item.State = result.Status switch
            {
                CompressionStatus.Optimized or CompressionStatus.Converted => FileItemState.Done,
                CompressionStatus.Failed => FileItemState.Error,
                _ => FileItemState.Skipped,
            };
        }
        catch (OperationCanceledException)
        {
            item.State = FileItemState.Skipped;
        }
        catch (Exception ex)
        {
            item.Result = new CompressionResult(item.Path, CompressionStatus.Failed, 0, 0, Message: ex.Message);
            item.State = FileItemState.Error;
        }
        finally
        {
            slots.Release();
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var results = Files.Select(f => f.Result).OfType<CompressionResult>().ToList();
        if (Files.Count == 0)
        {
            // The footer shows the credit instead: the drop hint is already in the middle of the window.
            Summary = string.Empty;
            IsBusy = false;
            return;
        }

        var saved = results.Sum(r => r.SavedBytes);
        var total = results.Sum(r => r.OriginalSize);
        var ratio = total > 0 ? (double)saved / total : 0;
        var working = Files.Count(f => f.State == FileItemState.Working);
        var waiting = Files.Count(f => f.State == FileItemState.Pending);
        IsBusy = working + waiting > 0;
        if (!IsBusy)
        {
            Summary = L10n.T(
                $"{L10n.Size(saved)} saved out of {L10n.Size(total)} ({L10n.Percent(ratio)}).",
                $"{L10n.Size(saved)} économisés sur {L10n.Size(total)} ({L10n.Percent(ratio)}).");
            return;
        }

        // "3 being compressed, 12 waiting · 4.2 MB saved so far"
        var progress = L10n.T(
            $"{working} being compressed",
            working == 1 ? "1 en cours de compression" : $"{working} en cours de compression");
        if (waiting > 0) progress += L10n.T($", {waiting} waiting", $", {waiting} en attente");
        Summary = saved > 0
            ? progress + L10n.T($" · {L10n.Size(saved)} saved so far", $" · {L10n.Size(saved)} économisés pour l'instant")
            : progress;
    }
}
