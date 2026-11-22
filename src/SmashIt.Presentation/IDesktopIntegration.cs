using SmashIt.Core.Shell;

namespace SmashIt.Presentation;

/// <summary>What each desktop app plugs into the shared view-models: the pieces that depend on its UI toolkit or system.</summary>
public interface IDesktopIntegration
{
    /// <summary>
    /// The row thumbnail of <paramref name="path"/>, as the UI toolkit's image object, or <c>null</c> to keep the
    /// kind icon. Called on the UI thread, before the file is compressed (the original may then go to the trash).
    /// </summary>
    Task<object?> LoadThumbnailAsync(string path);

    /// <summary>The file manager's "Compress with Smash It" entry, or <c>null</c> where there is none (yet).</summary>
    IContextMenu? ContextMenu { get; }
}
