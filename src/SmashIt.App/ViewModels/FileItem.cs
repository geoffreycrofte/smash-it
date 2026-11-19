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

    /// <summary>Shell thumbnail (images, videos, and PDFs when a PDF handler provides previews).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasThumbnail), nameof(ShowKindGlyph))]
    public partial Microsoft.UI.Xaml.Media.ImageSource? Thumbnail { get; set; }

    public bool HasThumbnail => Thumbnail is not null;

    public bool ShowKindGlyph => Thumbnail is null;

    /// <summary>Loads the thumbnail; awaited before compression so it is read from the original file.</summary>
    public Task? ThumbnailLoading { get; set; }

    /// <summary>Segoe Fluent Icons glyph for the kind of file, shown when there is no thumbnail.</summary>
    public string KindGlyph => Kind switch
    {
        MediaKind.Image => "",    // Photo
        MediaKind.Document => "", // PDF
        MediaKind.Video => "",    // Video
        _ => "",                  // Page
    };

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
        ? L10n.Size(SafeLength(Path))
        : L10n.Size(Result.OriginalSize);

    public string FinalSizeText => Result?.Status switch
    {
        CompressionStatus.Optimized or CompressionStatus.Converted => L10n.Size(Result.FinalSize),
        _ => "—",
    };

    public string GainText => State switch
    {
        FileItemState.Pending => L10n.T("Waiting", "En attente"),
        FileItemState.Working => Progress > 0
            ? $"{L10n.T("Compressing…", "Compression…")} {L10n.Percent(Progress / 100)}"
            : L10n.T("Compressing…", "Compression…"),
        _ => Result?.Status switch
        {
            CompressionStatus.Optimized => $"−{L10n.Percent(Result.SavedRatio)}",
            CompressionStatus.Converted => Result.SavedRatio >= 0
                ? L10n.T($"Converted, −{L10n.Percent(Result.SavedRatio)}", $"Converti, −{L10n.Percent(Result.SavedRatio)}")
                : L10n.T($"Converted, +{L10n.Percent(-Result.SavedRatio)}", $"Converti, +{L10n.Percent(-Result.SavedRatio)}"),
            CompressionStatus.AlreadyOptimal when Result.OutputPath is not null =>
                L10n.T("Already optimal, copied", "Déjà optimal, copié"),
            CompressionStatus.AlreadyOptimal => L10n.T("Already optimal", "Déjà optimal"),
            CompressionStatus.Unsupported => L10n.T("Not supported", "Non pris en charge"),
            CompressionStatus.Cancelled => L10n.T("Cancelled", "Annulé"),
            _ => L10n.T("Error", "Erreur"),
        },
    };

    public string? Message => Result switch
    {
        null => null,
        { OutputPath: { } output } when output != Path => $"→ {System.IO.Path.GetFileName(output)}" + (Result.Message is { } m ? Environment.NewLine + m : ""),
        _ => Result.Message,
    };

    /// <summary>
    /// What a screen reader says for the row (the list reads the item's text): "photo.jpg, 2.3 MB → 1.1 MB, −52 %",
    /// or "photo.jpg, Waiting" while nothing is done yet. Without it, the class name was read.
    /// </summary>
    public override string ToString() => Result?.Status is CompressionStatus.Optimized or CompressionStatus.Converted
        ? $"{Name}, {OriginalSizeText} → {FinalSizeText}, {GainText}"
        : $"{Name}, {GainText}";

    private static long SafeLength(string path)
    {
        try { return new FileInfo(path).Length; }
        catch (IOException) { return 0; }
    }
}
