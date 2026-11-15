using CommunityToolkit.Mvvm.ComponentModel;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;

namespace SmashIt.App.ViewModels;

public enum FileItemState
{
    Pending,
    Working,
    Done,
    Skipped,
    Error,
}

/// <summary>One row of the file list.</summary>
public sealed partial class FileItem(string path) : ObservableObject
{
    public string Path { get; } = path;

    public string Name { get; } = System.IO.Path.GetFileName(path);

    public string Folder { get; } = System.IO.Path.GetDirectoryName(path) ?? string.Empty;

    public MediaKind Kind { get; } = MediaFormatInfo.KindOf(MediaFormatInfo.FromExtension(path));

    /// <summary>Temporary copy of the image before compression, for the before/after preview.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanPreview))]
    public partial string? PreviewOriginalPath { get; set; }

    /// <summary>The compressed image exists and its "before" copy too.</summary>
    public bool CanPreview =>
        Kind == MediaKind.Image
        && PreviewOriginalPath is not null
        && Result is { Status: CompressionStatus.Optimized or CompressionStatus.Converted, OutputPath: not null };

    /// <summary>0–100 while a long job (video) runs.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(GainText))]
    public partial double Progress { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsWorking), nameof(StatusGlyph), nameof(OriginalSizeText), nameof(FinalSizeText), nameof(GainText))]
    public partial FileItemState State { get; set; } = FileItemState.Pending;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(OriginalSizeText), nameof(FinalSizeText), nameof(GainText), nameof(StatusGlyph), nameof(Message), nameof(CanPreview))]
    public partial CompressionResult? Result { get; set; }

    public bool IsWorking => State == FileItemState.Working;

    /// <summary>Segoe Fluent Icons glyph for the status column.</summary>
    public string StatusGlyph => State switch
    {
        FileItemState.Done => "",     // CheckMark
        FileItemState.Skipped => "",  // Remove
        FileItemState.Error => "",    // Error
        _ => string.Empty,
    };

    public string OriginalSizeText => Result is null
        ? SizeFormatter.Format(SafeLength(Path))
        : SizeFormatter.Format(Result.OriginalSize);

    public string FinalSizeText => Result?.Status switch
    {
        CompressionStatus.Optimized or CompressionStatus.Converted => SizeFormatter.Format(Result.FinalSize),
        _ => "—",
    };

    public string GainText => State switch
    {
        FileItemState.Pending => "En attente",
        FileItemState.Working => Progress > 0 ? $"Compression… {Progress:0} %" : "Compression…",
        _ => Result?.Status switch
        {
            CompressionStatus.Optimized => $"−{Result.SavedRatio * 100:0.#} %",
            CompressionStatus.Converted => Result.SavedRatio >= 0
                ? $"Converti, −{Result.SavedRatio * 100:0.#} %"
                : $"Converti, +{-Result.SavedRatio * 100:0.#} %",
            CompressionStatus.AlreadyOptimal => "Déjà optimal",
            CompressionStatus.Unsupported => "Non pris en charge",
            CompressionStatus.Cancelled => "Annulé",
            _ => "Erreur",
        },
    };

    public string? Message => Result switch
    {
        null => null,
        { OutputPath: { } output } when output != Path => $"→ {System.IO.Path.GetFileName(output)}" + (Result.Message is { } m ? Environment.NewLine + m : ""),
        _ => Result.Message,
    };

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (IOException) { return 0; }
    }
}
