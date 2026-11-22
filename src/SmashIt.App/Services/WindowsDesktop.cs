using Microsoft.UI.Xaml.Controls;
using SmashIt.Core.Shell;
using SmashIt.Presentation;
using SmashIt.Presentation.ViewModels;

namespace SmashIt.App.Services;

/// <summary>The Windows pieces of the shared view-models: shell thumbnails and the Explorer menu.</summary>
internal sealed class WindowsDesktop : IDesktopIntegration
{
    public IContextMenu? ContextMenu { get; } = ExplorerContextMenu.ForCurrentUser();

    public Task<object?> LoadThumbnailAsync(string path) => Thumbnails.LoadAsync(path);

    /// <summary>For <c>x:Bind</c>: the update banner's look, as an InfoBar severity (same order).</summary>
    public static InfoBarSeverity Severity(BannerSeverity severity) => (InfoBarSeverity)(int)severity;
}
