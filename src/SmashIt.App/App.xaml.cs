using Microsoft.UI.Xaml;
using SmashIt.App.Services;
using SmashIt.App.ViewModels;

namespace SmashIt.App;

public partial class App : Application
{
    /// <summary>How long the splash stays up at least, counted from when it appeared.</summary>
    private static readonly TimeSpan SplashMinimum = TimeSpan.FromMilliseconds(1200);

    private MainViewModel? _viewModel;
    private MainWindow? _window;

    /// <summary>Shown by <see cref="Program"/> before WinUI starts; closed once the main window is up.</summary>
    internal static SplashScreen? Splash { get; set; }

    public App() => InitializeComponent();

    protected override async void OnLaunched(LaunchActivatedEventArgs args)
    {
        _viewModel = new MainViewModel();
        _viewModel.LanguageChanged += RebuildMainWindow;
        var window = new MainWindow(_viewModel);
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
        if (files.Length > 0) _viewModel.AddPaths(files);

        if (Splash is { } shown)
        {
            Splash = null;
            await shown.FadeOutAsync();
        }
    }

    /// <summary>
    /// The texts are read when a window is built, so a new language means a new window: same files,
    /// same settings, same place on screen.
    /// </summary>
    private void RebuildMainWindow()
    {
        if (_window is not { } old || _viewModel is null) return;
        var replacement = new MainWindow(_viewModel);
        replacement.AppWindow.MoveAndResize(new Windows.Graphics.RectInt32(
            old.AppWindow.Position.X, old.AppWindow.Position.Y, old.AppWindow.Size.Width, old.AppWindow.Size.Height));
        _window = replacement;
        replacement.Activate();
        old.CloseForReplacement();
    }
}
