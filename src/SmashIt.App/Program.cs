using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using SmashIt.App.Services;

namespace SmashIt.App;

/// <summary>
/// Entry point (replaces the XAML-generated one, see DISABLE_XAML_GENERATED_MAIN): shows the splash screen
/// before WinUI loads, so it covers the whole startup.
/// </summary>
public static class Program
{
    [STAThread]
    private static void Main()
    {
        WinRT.ComWrappersSupport.InitializeComWrappers();
        L10n.Use(AppSettings.Current.Language);
        App.Splash = SplashScreen.TryShow();

        Application.Start(_ =>
        {
            var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
            SynchronizationContext.SetSynchronizationContext(context);
            new App();
        });
    }
}
