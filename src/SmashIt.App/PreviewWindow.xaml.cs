using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SmashIt.App.ViewModels;
using Windows.Foundation;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.System;

namespace SmashIt.App;

/// <summary>
/// Before/after comparison: both images stacked, the "before" one clipped left of a split line that the user
/// drags with the handle (or the mouse anywhere, or the arrow keys).
/// </summary>
public sealed partial class PreviewWindow : Window
{
    private const double KeyStep = 0.05;
    private const double FineKeyStep = 0.01;
    private const double BadgeMinRoom = 90;

    private readonly string _beforePath;
    private readonly string _afterPath;
    private double _ratio = 0.5;
    private bool _dragging;
    private bool _loaded;

    public PreviewWindow(FileItem item, string beforePath, string afterPath)
    {
        InitializeComponent();
        _beforePath = beforePath;
        _afterPath = afterPath;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1100, 780));
        Services.AppIcon.Apply(AppWindow, TitleBarIcon);

        var title = $"Avant / après — {item.Name}";
        Title = title;
        TitleText.Text = title;
        FileNameText.Text = Path.GetFileName(afterPath);
        SizesText.Text = $"{item.OriginalSizeText} → {item.FinalSizeText}   ({item.GainText})";
        BeforeBadgeText.Text = $"Avant · {item.OriginalSizeText}";
        AfterBadgeText.Text = $"Après · {item.FinalSizeText}";
        SetOverlayVisible(false);

        Stage.PointerPressed += OnStagePointerPressed;
        _ = LoadAsync();
    }

    private async Task LoadAsync()
    {
        try
        {
            var before = await DecodeAsync(_beforePath);
            var after = await DecodeAsync(_afterPath);

            var beforeSource = new SoftwareBitmapSource();
            await beforeSource.SetBitmapAsync(before);
            var afterSource = new SoftwareBitmapSource();
            await afterSource.SetBitmapAsync(after);

            // Both images are drawn in the box of the compressed one (they have the same size unless
            // the engine resized, in which case "before" is stretched onto it).
            ImageHost.Width = after.PixelWidth;
            ImageHost.Height = after.PixelHeight;
            BeforeImage.Source = beforeSource;
            AfterImage.Source = afterSource;
            before.Dispose();
            after.Dispose();

            LoadingPanel.Visibility = Visibility.Collapsed;
            _loaded = true;
            SetOverlayVisible(true);
            // Layout of the Viewbox happens on the next pass.
            ImageBox.LayoutUpdated += OnFirstLayout;
            Handle.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            LoadingPanel.Children.OfType<ProgressRing>().First().IsActive = false;
            StatusText.Text = $"Aperçu impossible : {ex.Message}";
        }
    }

    private void OnFirstLayout(object? sender, object e)
    {
        ImageBox.LayoutUpdated -= OnFirstLayout;
        UpdateSplit();
    }

    /// <summary>Decodes with the orientation applied and colors converted to sRGB, the same way for both images.</summary>
    private static async Task<SoftwareBitmap> DecodeAsync(string path)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            new BitmapTransform(),
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.ColorManageToSRgb);
    }

    /// <summary>Where the images are drawn inside the stage, in stage coordinates.</summary>
    private Rect ImageBounds() =>
        ImageHost.ActualWidth > 0
            ? ImageHost.TransformToVisual(Stage).TransformBounds(new Rect(0, 0, ImageHost.ActualWidth, ImageHost.ActualHeight))
            : Rect.Empty;

    private void UpdateSplit()
    {
        if (!_loaded) return;
        var bounds = ImageBounds();
        if (bounds.IsEmpty || bounds.Width <= 0) return;

        // Clip in the image's own coordinates (the Viewbox scales them).
        BeforeImage.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, _ratio * ImageHost.ActualWidth, ImageHost.ActualHeight),
        };

        var x = bounds.X + _ratio * bounds.Width;
        Canvas.SetLeft(SplitLine, x - SplitLine.Width / 2);
        Canvas.SetTop(SplitLine, bounds.Y);
        SplitLine.Height = bounds.Height;

        Canvas.SetLeft(Handle, x - Handle.Width / 2);
        Canvas.SetTop(Handle, bounds.Y + bounds.Height / 2 - Handle.Height / 2);

        Canvas.SetLeft(BeforeBadge, bounds.X + 12);
        Canvas.SetTop(BeforeBadge, bounds.Y + 12);
        BeforeBadge.Opacity = x - bounds.X > BadgeMinRoom ? 1 : 0;

        AfterBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(AfterBadge, bounds.Right - AfterBadge.DesiredSize.Width - 12);
        Canvas.SetTop(AfterBadge, bounds.Y + 12);
        AfterBadge.Opacity = bounds.Right - x > BadgeMinRoom ? 1 : 0;
    }

    private void SetRatio(double ratio)
    {
        _ratio = Math.Clamp(ratio, 0, 1);
        UpdateSplit();
    }

    private void SetRatioFromPointer(PointerRoutedEventArgs e)
    {
        if (ImageHost.ActualWidth <= 0) return;
        SetRatio(e.GetCurrentPoint(ImageHost).Position.X / ImageHost.ActualWidth);
    }

    private void SetOverlayVisible(bool visible) =>
        Overlay.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

    private void OnStageSizeChanged(object sender, SizeChangedEventArgs e) => UpdateSplit();

    private void OnStagePointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!_loaded) return;
        _dragging = true;
        Stage.CapturePointer(e.Pointer);
        SetRatioFromPointer(e);
        Handle.Focus(FocusState.Pointer);
    }

    private void OnStagePointerMoved(object sender, PointerRoutedEventArgs e)
    {
        if (_dragging) SetRatioFromPointer(e);
    }

    private void OnStagePointerReleased(object sender, PointerRoutedEventArgs e)
    {
        _dragging = false;
        Stage.ReleasePointerCapture(e.Pointer);
    }

    private void OnStagePointerCaptureLost(object sender, PointerRoutedEventArgs e) => _dragging = false;

    private void OnStageDoubleTapped(object sender, DoubleTappedRoutedEventArgs e) => SetRatio(0.5);

    private void OnEscapeInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        args.Handled = true;
        Close();
    }

    private void OnHandleKeyDown(object sender, KeyRoutedEventArgs e)
    {
        var shift = Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)
            .HasFlag(Windows.UI.Core.CoreVirtualKeyStates.Down);
        var step = shift ? FineKeyStep : KeyStep;
        switch (e.Key)
        {
            case VirtualKey.Left: SetRatio(_ratio - step); break;
            case VirtualKey.Right: SetRatio(_ratio + step); break;
            case VirtualKey.Home: SetRatio(0); break;
            case VirtualKey.End: SetRatio(1); break;
            default: return;
        }
        e.Handled = true;
    }
}
