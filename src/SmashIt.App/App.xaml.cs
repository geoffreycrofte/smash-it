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

        // Explorer starts the app once per selected file, a few milliseconds apart: the files are gathered
        // until the launches stop, so 40 selected photos make one batch (and one question), not 40.
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        var incoming = new List<string>();
        var gather = dispatcher.CreateTimer();
        gather.Interval = TimeSpan.FromMilliseconds(400);
        gather.IsRepeating = false;
        gather.Tick += (_, _) =>
        {
            var batch = incoming.ToArray();
            incoming.Clear();
            if (batch.Length > 0) _ = _viewModel.AddPathsAsync(batch);
        };
        void Receive(IEnumerable<string> paths)
        {
            incoming.AddRange(paths);
            gather.Stop();
            gather.Start();
        }

        // Files passed on the command line ("Compress with Smash It") are processed right away.
        Receive(Environment.GetCommandLineArgs().Skip(1));

        // Later launches land in this window.
        SingleInstance.OnLaunchForwarded(paths => dispatcher.TryEnqueue(() =>
        {
            Receive(paths);
            _window?.BringToFront();
        }));

        if (Splash is { } shown)
        {
            Splash = null;
            await shown.FadeOutAsync();
        }

        // The installer replaces the app's files and starts it again: close everything (settings are saved).
        _viewModel.Updates.ExitRequested += () =>
        {
            _window?.Close();
            Exit();
        };
        await _viewModel.Updates.StartupAsync();
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
