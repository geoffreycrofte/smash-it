using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SmashIt.App.Services;
using SmashIt.Core.Shell;

namespace SmashIt.App;

/// <summary>
/// Entry point (replaces the XAML-generated one, see DISABLE_XAML_GENERATED_MAIN): shows the splash screen
/// before WinUI loads, so it covers the whole startup.
/// </summary>
public static class Program
{
    /// <summary>Adds the Explorer right-click entry for the current user, then exits (used by the installer).</summary>
    public const string RegisterContextMenu = "--register-context-menu";

    /// <summary>Removes it, then exits (used by the uninstaller).</summary>
    public const string UnregisterContextMenu = "--unregister-context-menu";

    [STAThread]
    private static int Main(string[] args)
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        L10n.Use(AppSettings.Current.Language);

        if (args is [RegisterContextMenu])
        {
            ExplorerContextMenu.ForCurrentUser().Register(Environment.ProcessPath!, Strings.ContextMenuLabel);
            return 0;
        }
        if (args is [UnregisterContextMenu])
        {
            ExplorerContextMenu.ForCurrentUser().Unregister();
            return 0;
        }

        // Another window is open: it takes these files.
        if (!SingleInstance.TryBecomePrimary()) return 0;

        App.Splash = SplashScreen.TryShow();

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
        return 0;
    }
}
