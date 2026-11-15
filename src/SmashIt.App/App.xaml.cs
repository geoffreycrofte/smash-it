using Microsoft.UI.Xaml;
using SmashIt.App.Services;

namespace SmashIt.App;

public partial class App : Application
{
    /// <summary>How long the splash stays up at least, counted from when it appeared.</summary>
    private static readonly TimeSpan SplashMinimum = TimeSpan.FromMilliseconds(1200);

    private Window? _window;

    /// <summary>Shown by <see cref="Program"/> before WinUI starts; closed once the main window is up.</summary>
    internal static SplashScreen? Splash { get; set; }

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        _window = window;

        // The main window shows up when the splash leaves, not behind it.
        if (Splash is { } splash)
        {
            var wait = SplashMinimum - splash.Elapsed;
            if (wait > TimeSpan.Zero) await Task.Delay(wait);
        }
        window.Activate();

        // Files passed on the command line (e.g. "Open with Smash It") are processed right away.
        var files = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (files.Length > 0) window.ViewModel.AddPaths(files);

        if (Splash is { } shown)
        {
            Splash = null;
            await shown.FadeOutAsync();
        }
    }
}
