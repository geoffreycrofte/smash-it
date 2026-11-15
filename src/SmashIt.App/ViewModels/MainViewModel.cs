using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SmashIt.Core;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.IO;

namespace SmashIt.App.ViewModels;

public sealed record OutputFormatChoice(string Label, MediaFormat? Format)
{
    public override string ToString() => Label;
}

public sealed record EffortChoice(string Label, CompressionEffort Effort)
{
    public override string ToString() => Label;
}

public sealed partial class MainViewModel : ObservableObject
{
    private readonly CompressionPipeline _pipeline = SmashItEngine.CreateDefaultPipeline();
    private readonly SemaphoreSlim _slots = new(Math.Max(1, Environment.ProcessorCount - 1));
    private CancellationTokenSource _cts = new();

    public ObservableCollection<FileItem> Files { get; } = [];

    public IReadOnlyList<OutputFormatChoice> OutputFormats { get; } =
    [
        new("Conserver le format", null),
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
        new("Rapide", CompressionEffort.Fast),
        new("Équilibré", CompressionEffort.Balanced),
        new("Maximum", CompressionEffort.Maximum),
    ];

    [ObservableProperty]
    public partial bool IsLossy { get; set; }

    [ObservableProperty]
    public partial double Quality { get; set; } = 85;

    [ObservableProperty]
    public partial OutputFormatChoice SelectedOutputFormat { get; set; }

    [ObservableProperty]
    public partial EffortChoice SelectedEffort { get; set; }

    [ObservableProperty]
    public partial bool KeepOriginal { get; set; }

    [ObservableProperty]
    public partial bool StripMetadata { get; set; } = true;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsEmpty))]
    public partial int FileCount { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "Déposez des images ou des PDF ici pour les compresser.";

    public bool IsEmpty => FileCount == 0;

    public MainViewModel()
    {
        SelectedOutputFormat = OutputFormats[0];
        SelectedEffort = Efforts[1];
        Files.CollectionChanged += (_, _) => FileCount = Files.Count;
    }

    private CompressionOptions CurrentOptions() => new()
    {
        Mode = IsLossy ? CompressionMode.Lossy : CompressionMode.Lossless,
        Quality = (int)Math.Round(Quality),
        Effort = SelectedEffort.Effort,
        TargetFormat = SelectedOutputFormat.Format,
        StripMetadata = StripMetadata,
        OriginalHandling = KeepOriginal ? OriginalHandling.KeepAlongside : OriginalHandling.RecycleBin,
    };

    /// <summary>Adds files and folders and starts processing them with the current settings.</summary>
    public void AddPaths(IEnumerable<string> paths)
    {
        var options = CurrentOptions();
        foreach (var path in InputCollector.Expand(paths))
        {
            var item = new FileItem(path);
            Files.Add(item);
            _ = ProcessAsync(item, options, _cts.Token);
        }
    }

    [RelayCommand]
    private void Reprocess()
    {
        var options = CurrentOptions();
        foreach (var item in Files.Where(f => f.State is not FileItemState.Working and not FileItemState.Pending).ToList())
        {
            // A converted file stays where it is; re-run on the original.
            item.Result = null;
            _ = ProcessAsync(item, options, _cts.Token);
        }
    }

    [RelayCommand]
    private void Clear()
    {
        _cts.Cancel();
        _cts = new CancellationTokenSource();
        Files.Clear();
        UpdateSummary();
    }

    /// <summary>Runs on the UI thread; the heavy work is pushed to the thread pool.</summary>
    private async Task ProcessAsync(FileItem item, CompressionOptions options, CancellationToken ct)
    {
        item.State = FileItemState.Pending;
        try
        {
            await _slots.WaitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        try
        {
            item.State = FileItemState.Working;
            var result = await Task.Run(() => _pipeline.ProcessAsync(item.Path, options, ct), ct);
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
            _slots.Release();
            UpdateSummary();
        }
    }

    private void UpdateSummary()
    {
        var results = Files.Select(f => f.Result).OfType<CompressionResult>().ToList();
        if (Files.Count == 0)
        {
            Summary = "Déposez des images ou des PDF ici pour les compresser.";
            return;
        }

        var saved = results.Sum(r => r.SavedBytes);
        var total = results.Sum(r => r.OriginalSize);
        var ratio = total > 0 ? (double)saved / total : 0;
        var remaining = Files.Count(f => f.State is FileItemState.Pending or FileItemState.Working);
        Summary = remaining > 0
            ? $"{remaining} fichier(s) en cours… {SizeFormatter.Format(saved)} économisés pour l'instant."
            : $"{SizeFormatter.Format(saved)} économisés sur {SizeFormatter.Format(total)} ({ratio * 100:0.#} %).";
    }
}
