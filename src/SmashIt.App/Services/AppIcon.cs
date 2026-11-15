using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;

namespace SmashIt.App.Services;

/// <summary>Applies Smash It's icon to a window (taskbar, Alt+Tab) and to its custom title bar.</summary>
internal static class AppIcon
{
    private static string AssetPath(string name) => Path.Combine(AppContext.BaseDirectory, "Assets", name);

    public static void Apply(AppWindow window, Image titleBarIcon)
    {
        var ico = AssetPath("SmashIt.ico");
        if (File.Exists(ico)) window.SetIcon(ico);

        var head = AssetPath("SmashIt-head-32.png");
        if (File.Exists(head)) titleBarIcon.Source = new BitmapImage(new Uri(head));
    }
}
