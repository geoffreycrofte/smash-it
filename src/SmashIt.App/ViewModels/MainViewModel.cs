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

/// <summary>A preset, or <c>null</c> for "Custom" (settings that match no preset).</summary>
public sealed record PresetChoice(PresetId? Id)
{
    public string Label => Id switch
    {
        PresetId.Lossless => L10n.T("Best quality (lossless)", "Qualité maximale (sans perte)"),
        PresetId.Web => L10n.T("Web", "Web"),
        PresetId.Email => L10n.T("Email (lightest)", "E-mail (le plus léger)"),
        PresetId.Archive => L10n.T("Archive (lossless, metadata kept)", "Archive (sans perte, métadonnées gardées)"),
        _ => L10n.T("Custom", "Personnalisé"),
    };

    public string Description => Id switch
    {
        PresetId.Lossless => L10n.T(
            "Files get smaller without any visible change. Metadata (camera, GPS…) is removed.",
            "Les fichiers s'allègent sans aucun changement visible. Les métadonnées (appareil, GPS…) sont retirées."),
        PresetId.Web => L10n.T(
            "Much lighter images, PDFs and videos for websites, sharp on screen. Large photos are brought down to 2560 px, videos to 1080p.",
            "Images, PDF et vidéos bien plus légers pour un site web, nets à l'écran. Les grandes photos sont ramenées à 2560 px, les vidéos à 1080p."),
        PresetId.Email => L10n.T(
            "The smallest files, to send by email or message, good for viewing on screen. Photos are brought down to 1920 px, videos to 720p.",
            "Les fichiers les plus petits, à envoyer par e-mail ou message, bons à l'écran. Les photos sont ramenées à 1920 px, les vidéos à 720p."),
        PresetId.Archive => L10n.T(
            "Nothing is lost: same quality and every piece of metadata kept, only the storage is optimized.",
            "Rien n'est perdu : même qualité et toutes les métadonnées gardées, seul le stockage est optimisé."),
        _ => L10n.T(
            "Your own settings, below.",
            "Vos propres réglages, ci-dessous."),
    };

    public override string ToString() => Label;
}

/// <summary>A PDF image resolution cap, in pixels per inch; 0 keeps every image as it is.</summary>
/// <summary>An image size cap, as the long side in pixels; 0 keeps the size.</summary>
public sealed record ImageSizeChoice(int LongSide)
{
    public string Label => LongSide == 0
        ? L10n.T("Keep the size", "Conserver la taille")
        : $"{LongSide} px max";

    public override string ToString() => Label;
}

/// <summary>A video resolution cap, as the short side of the picture in pixels; 0 keeps the resolution.</summary>
public sealed record VideoResolutionChoice(int ShortSide)
{
    public string Label => ShortSide switch
    {
        0 => L10n.T("Keep the resolution", "Conserver la résolution"),
        1080 => L10n.T("1080p (Full HD)", "1080p (Full HD)"),
        720 => L10n.T("720p (lighter)", "720p (plus léger)"),
        _ => $"{ShortSide}p",
    };

    public override string ToString() => Label;
}

public sealed record PdfResolutionChoice(int Dpi)
{
    public string Label => Dpi switch
    {
        0 => L10n.T("Keep the resolution", "Conserver la résolution"),
        >= 300 => L10n.T($"{Dpi} dpi (print)", $"{Dpi} ppp (impression)"),
        _ => L10n.T($"{Dpi} dpi (screen)", $"{Dpi} ppp (écran)"),
    };

    public override string ToString() => Label;
}

/// <summary>The answer to "where do the compressed files go?".</summary>
public sealed record OutputLocationAnswer(bool InCompressedFolder, bool CopyAlreadyOptimal, bool DontAskAgain);

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

    /// <summary>New versions: the banner above the list and the "Updates" part of Settings.</summary>
    public UpdateViewModel Updates { get; }

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

    // Preset: follows the settings below ("Custom" when they match none), and sets them when chosen.
    public IReadOnlyList<PresetChoice> Presets { get; } =
        [new(PresetId.Lossless), new(PresetId.Web), new(PresetId.Email), new(PresetId.Archive), new(null)];

    [ObservableProperty]
    public partial PresetChoice SelectedPreset { get; set; }

    /// <summary>The settings a preset covers: a change to one of them may change the preset shown.</summary>
    private static readonly HashSet<string> PresetProperties =
    [
        nameof(ImageLossy), nameof(ImageQuality), nameof(StripMetadata), nameof(PdfLossy), nameof(PdfQuality),
        nameof(SelectedPdfResolution), nameof(VideoQuality), nameof(SelectedVideoResolution), nameof(SelectedImageSize),
    ];

    private bool _applyingPreset;

    partial void OnSelectedPresetChanged(PresetChoice value)
    {
        if (_applyingPreset || value?.Id is not { } id) return;
        var preset = CompressionPresets.All[id];
        _applyingPreset = true;
        try
        {
            ImageLossy = preset.ImageLossy;
            ImageQuality = preset.ImageQuality;
            SelectedImageSize = ImageSizes.FirstOrDefault(s => s.LongSide == preset.ImageMaxSide) ?? ImageSizes[0];
            StripMetadata = preset.StripMetadata;
            PdfLossy = preset.PdfLossy;
            PdfQuality = preset.PdfQuality;
            SelectedPdfResolution = PdfResolutions.FirstOrDefault(r => r.Dpi == preset.PdfMaxImageDpi) ?? SelectedPdfResolution;
            VideoQuality = preset.VideoQuality;
            SelectedVideoResolution = VideoResolutions.FirstOrDefault(r => r.ShortSide == preset.VideoMaxShortSide) ?? VideoResolutions[0];
        }
        finally
        {
            _applyingPreset = false;
        }
    }

    /// <summary>Shows the preset the current settings amount to, without applying anything.</summary>
    private void UpdateSelectedPreset()
    {
        if (_applyingPreset || SelectedPdfResolution is null || SelectedVideoResolution is null || SelectedImageSize is null) return;
        var id = CompressionPresets.Match(new PresetSettings(
            ImageLossy, (int)Math.Round(ImageQuality), StripMetadata, PdfLossy, (int)Math.Round(PdfQuality),
            SelectedPdfResolution.Dpi, (int)Math.Round(VideoQuality), SelectedVideoResolution.ShortSide, SelectedImageSize.LongSide));
        var choice = Presets.First(p => p.Id == id);
        if (choice == SelectedPreset) return;
        _applyingPreset = true;
        try { SelectedPreset = choice; }
        finally { _applyingPreset = false; }
    }

    // Images
    [ObservableProperty]
    public partial bool ImageLossy { get; set; }

    [ObservableProperty]
    public partial double ImageQuality { get; set; } = 85;

    public IReadOnlyList<ImageSizeChoice> ImageSizes { get; } = [new(0), new(3840), new(2560), new(1920), new(1280)];

    /// <summary>Lossy only: images larger than this (long side) are scaled down to it.</summary>
    [ObservableProperty]
    public partial ImageSizeChoice SelectedImageSize { get; set; }

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

    public IReadOnlyList<PdfResolutionChoice> PdfResolutions { get; } = [new(0), new(300), new(150)];

    /// <summary>Lossy only: images far finer than their size on the page are scaled down to this.</summary>
    [ObservableProperty]
    public partial PdfResolutionChoice SelectedPdfResolution { get; set; }

    // Video (always lossy)
    [ObservableProperty]
    public partial double VideoQuality { get; set; } = 70;

    [ObservableProperty]
    public partial VideoCodecChoice SelectedVideoCodec { get; set; }

    public IReadOnlyList<VideoResolutionChoice> VideoResolutions { get; } = [new(0), new(1080), new(720)];

    /// <summary>Videos larger than this (4K phone videos, say) are scaled down to it.</summary>
    [ObservableProperty]
    public partial VideoResolutionChoice SelectedVideoResolution { get; set; }

    // All files
    // On by default: nothing is replaced until the user asks for it.
    [ObservableProperty]
    public partial bool KeepOriginal { get; set; } = true;

    /// <summary>Where kept-original results go: 0 next to the original ("name-compressed.ext"), 1 in a "compressed" folder.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SavesInCompressedFolder))]
    public partial int OutputLocationIndex { get; set; }

    public bool SavesInCompressedFolder => OutputLocationIndex == 1;

    /// <summary>With the "compressed" folder: files that are already optimal are copied there too.</summary>
    [ObservableProperty]
    public partial bool CopyAlreadyOptimal { get; set; }

    /// <summary>Ask where the results go when several files are added at once.</summary>
    [ObservableProperty]
    public partial bool AskOutputLocation { get; set; } = true;

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
        nameof(ImageLossy), nameof(ImageQuality), nameof(SelectedImageSize), nameof(SelectedOutputFormat), nameof(SelectedEffort),
        nameof(StripMetadata), nameof(PdfLossy), nameof(PdfQuality), nameof(SelectedPdfResolution), nameof(VideoQuality),
        nameof(SelectedVideoCodec), nameof(SelectedVideoResolution), nameof(KeepOriginal), nameof(OutputLocationIndex), nameof(CopyAlreadyOptimal),
        nameof(AskOutputLocation),
    ];

    // Sliders change many times per second: the file is written once they settle.
    private readonly Microsoft.UI.Dispatching.DispatcherQueueTimer? _saveTimer;

    public MainViewModel()
    {
        Updates = new UpdateViewModel(() => IsBusy);
        var settings = AppSettings.Current;
        ImageLossy = settings.ImageLossy;
        ImageQuality = Math.Clamp(settings.ImageQuality, 1, 100);
        SelectedImageSize = ImageSizes.FirstOrDefault(s => s.LongSide == settings.ImageMaxSide) ?? ImageSizes[0];
        SelectedOutputFormat = OutputFormats.FirstOrDefault(f => f.Format == settings.OutputFormat) ?? OutputFormats[0];
        SelectedEffort = Efforts.FirstOrDefault(e => e.Effort == settings.Effort) ?? Efforts[1];
        StripMetadata = settings.StripMetadata;
        PdfLossy = settings.PdfLossy;
        PdfQuality = Math.Clamp(settings.PdfQuality, 1, 100);
        SelectedPdfResolution = PdfResolutions.FirstOrDefault(r => r.Dpi == settings.PdfMaxImageDpi) ?? PdfResolutions[^1];
        VideoQuality = Math.Clamp(settings.VideoQuality, 1, 100);
        SelectedVideoCodec = VideoCodecs.FirstOrDefault(c => c.Codec == settings.VideoCodec) ?? VideoCodecs[0];
        SelectedVideoResolution = VideoResolutions.FirstOrDefault(r => r.ShortSide == settings.VideoMaxShortSide) ?? VideoResolutions[0];
        KeepOriginal = settings.KeepOriginal;
        OutputLocationIndex = settings.SaveInCompressedFolder ? 1 : 0;
        CopyAlreadyOptimal = settings.CopyAlreadyOptimal;
        AskOutputLocation = settings.AskOutputLocation;
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
        UpdateSelectedPreset();
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is { } changed && PresetProperties.Contains(changed)) UpdateSelectedPreset();
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
        settings.ImageMaxSide = SelectedImageSize.LongSide;
        settings.OutputFormat = SelectedOutputFormat.Format;
        settings.Effort = SelectedEffort.Effort;
        settings.StripMetadata = StripMetadata;
        settings.PdfLossy = PdfLossy;
        settings.PdfQuality = (int)Math.Round(PdfQuality);
        settings.PdfMaxImageDpi = SelectedPdfResolution.Dpi;
        settings.VideoQuality = (int)Math.Round(VideoQuality);
        settings.VideoCodec = SelectedVideoCodec.Codec;
        settings.VideoMaxShortSide = SelectedVideoResolution.ShortSide;
        settings.KeepOriginal = KeepOriginal;
        settings.SaveInCompressedFolder = OutputLocationIndex == 1;
        settings.CopyAlreadyOptimal = CopyAlreadyOptimal;
        settings.AskOutputLocation = AskOutputLocation;
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
            KeptOriginalFolder = KeepOriginal && OutputLocationIndex == 1 ? CompressionOptions.CompressedFolderName : null,
            CopyAlreadyOptimal = CopyAlreadyOptimal,
        };
        return kind switch
        {
            MediaKind.Document => common with
            {
                Mode = PdfLossy ? CompressionMode.Lossy : CompressionMode.Lossless,
                Quality = (int)Math.Round(PdfQuality),
                PdfMaxImageDpi = SelectedPdfResolution.Dpi,
            },
            MediaKind.Video => common with
            {
                Mode = CompressionMode.Lossy,
                Quality = (int)Math.Round(VideoQuality),
                VideoCodec = SelectedVideoCodec.Codec,
                VideoMaxShortSide = SelectedVideoResolution.ShortSide,
            },
            _ => common with
            {
                Mode = ImageLossy ? CompressionMode.Lossy : CompressionMode.Lossless,
                Quality = (int)Math.Round(ImageQuality),
                ImageMaxSide = SelectedImageSize.LongSide,
                Effort = SelectedEffort.Effort,
                TargetFormat = SelectedOutputFormat.Format,
                StripMetadata = StripMetadata,
            },
        };
    }

    /// <summary>Adds files and folders and starts processing them with the current settings.</summary>
    /// <remarks>
    /// With the originals kept and several files at once, asks first where the results go (unless the user said
    /// not to ask again). Files arriving while the question is open join that batch.
    /// </remarks>
    public async Task AddPathsAsync(IEnumerable<string> paths)
    {
        // An earlier run's "compressed" folder is not compressed again when its parent folder is dropped.
        var files = InputCollector.Expand(paths, CompressionOptions.CompressedFolderName).ToList();
        if (files.Count == 0) return;

        if (_askingBatch is { } open)
        {
            open.AddRange(files);
            return;
        }

        if (KeepOriginal && AskOutputLocation && files.Count > 1 && AskWhereToSave is { } ask)
        {
            _askingBatch = files;
            OutputLocationAnswer? answer;
            try
            {
                answer = await ask(files);
            }
            finally
            {
                _askingBatch = null;
            }
            if (answer is null) return; // cancelled: nothing is added

            OutputLocationIndex = answer.InCompressedFolder ? 1 : 0;
            if (answer.InCompressedFolder) CopyAlreadyOptimal = answer.CopyAlreadyOptimal;
            if (answer.DontAskAgain) AskOutputLocation = false;
        }

        foreach (var path in files)
        {
            var item = new FileItem(path);
            Files.Add(item);
            item.ThumbnailLoading = Thumbnails.LoadAsync(item);
            _ = ProcessAsync(item, OptionsFor(item.Kind), _cts.Token);
        }
    }

    /// <summary>Shows the "where do the compressed files go?" question; <c>null</c> when cancelled. Set by the window.</summary>
    public Func<IReadOnlyList<string>, Task<OutputLocationAnswer?>>? AskWhereToSave { get; set; }

    private List<string>? _askingBatch;

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

    /// <summary>Stops the batch: waiting files are not started, the ones being compressed are abandoned (their originals stay as they were).</summary>
    [RelayCommand]
    private void Stop()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
    }

    private static CompressionResult Cancelled(FileItem item)
    {
        long size = 0;
        try { size = new FileInfo(item.Path).Length; } catch (IOException) { }
        return new(item.Path, CompressionStatus.Cancelled, size, size);
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
            // Stopped (or cleared) before its turn came.
            item.Result = Cancelled(item);
            item.State = FileItemState.Skipped;
            UpdateSummary();
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
            item.Result = Cancelled(item);
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
        // Stopped files were not compressed: they don't count in "saved out of".
        var results = Files.Select(f => f.Result).OfType<CompressionResult>()
            .Where(r => r.Status != CompressionStatus.Cancelled).ToList();
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
