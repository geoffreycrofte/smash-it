using Microsoft.UI.Xaml;

namespace SmashIt.App;

public partial class App : Application
{
    private Window? _window;

    public App() => InitializeComponent();

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        var window = new MainWindow();
        _window = window;
        window.Activate();

        // Files passed on the command line (e.g. "Open with Smash It") are processed right away.
        var files = Environment.GetCommandLineArgs().Skip(1).ToArray();
        if (files.Length > 0) window.ViewModel.AddPaths(files);
    }
}
