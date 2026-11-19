using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.UI.Xaml;
using Windows.UI.ViewManagement;

namespace SmashIt.App.Services;

/// <summary>
/// Windows' "Text size" setting (Settings → Accessibility → Text size), from 100 % to 225 %. Text grows with it
/// on its own; the few fixed widths of the window (list columns, settings panel) grow with it through
/// <see cref="Width"/> and <see cref="Length"/>, so nothing gets cut off.
/// </summary>
public sealed partial class TextScale : ObservableObject
{
    public static TextScale Current { get; } = new();

    // Kept: the change notification only comes while the UISettings object is alive.
    private readonly UISettings _settings = new();

    [ObservableProperty]
    public partial double Factor { get; set; } = 1;

    private TextScale()
    {
        Factor = _settings.TextScaleFactor;
#if DEBUG
        // Development only: try the layout at another text size without changing Windows' setting.
        if (double.TryParse(Environment.GetEnvironmentVariable("SMASHIT_TEXT_SCALE"), System.Globalization.CultureInfo.InvariantCulture, out var forced))
            Factor = forced;
#endif
        var dispatcher = Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread();
        _settings.TextScaleFactorChanged += (sender, _) => dispatcher?.TryEnqueue(() => Factor = sender.TextScaleFactor);
    }

    /// <summary>A column width designed at 100 % text size, at the current text size.</summary>
    public static GridLength Width(double designed, double factor) => new(Length(designed, factor));

    /// <summary>A length designed at 100 % text size, at the current text size.</summary>
    public static double Length(double designed, double factor) => Math.Round(designed * Math.Max(1, factor));
}
