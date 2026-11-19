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
    private int _pixelWidth;
    private int _pixelHeight;

    public PreviewWindow(FileItem item, string beforePath, string afterPath)
    {
        InitializeComponent();
        _beforePath = beforePath;
        _afterPath = afterPath;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1100, 780));
        Services.AppIcon.Apply(AppWindow, TitleBarIcon);

        var title = $"{Strings.PreviewTitle} — {item.Name}";
        Title = title;
        TitleText.Text = title;
        FileNameText.Text = Path.GetFileName(afterPath);
        SizesText.Text = $"{item.OriginalSizeText} → {item.FinalSizeText}   ({item.GainText})";
        BeforeBadgeText.Text = $"{Strings.Before} · {item.OriginalSizeText}";
        AfterBadgeText.Text = $"{Strings.After} · {item.FinalSizeText}";
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
            (_pixelWidth, _pixelHeight) = (after.PixelWidth, after.PixelHeight);
            ImageHost.Width = _pixelWidth;
            ImageHost.Height = _pixelHeight;
            BeforeImage.Source = beforeSource;
            AfterImage.Source = afterSource;
            before.Dispose();
            after.Dispose();

            LoadingPanel.Visibility = Visibility.Collapsed;
            _loaded = true;
            ActualSizeButton.IsEnabled = true;
            // Moved to a screen with another scaling: 100 % must stay one image pixel per screen pixel.
            Content.XamlRoot.Changed += (_, _) => { if (ActualSizeButton.IsChecked == true) ApplyZoom(); };
            SetOverlayVisible(true);
            // Layout of the Viewbox happens on the next pass.
            ImageBox.LayoutUpdated += OnFirstLayout;
            Handle.Focus(FocusState.Programmatic);
        }
        catch (Exception ex)
        {
            LoadingPanel.Children.OfType<ProgressRing>().First().IsActive = false;
            StatusText.Text = Strings.PreviewFailed(ex.Message);
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
        var image = ImageBounds();
        if (image.IsEmpty || image.Width <= 0) return;

        // Clip in the image's own coordinates (the Viewbox scales them).
        BeforeImage.Clip = new RectangleGeometry
        {
            Rect = new Rect(0, 0, _ratio * ImageHost.ActualWidth, ImageHost.ActualHeight),
        };

        // At 100 % the image is larger than the stage: line, handle and badges stay on the part in view.
        var top = Math.Max(image.Y, 0);
        var bottom = Math.Min(image.Bottom, Stage.ActualHeight);
        var left = Math.Max(image.X, 0);
        var right = Math.Min(image.Right, Stage.ActualWidth);
        if (bottom <= top || right <= left) return;

        var x = image.X + _ratio * image.Width;
        var inView = x >= left - 1 && x <= right + 1;
        SplitLine.Opacity = Handle.Opacity = inView ? 1 : 0;
        Canvas.SetLeft(SplitLine, x - SplitLine.Width / 2);
        Canvas.SetTop(SplitLine, top);
        SplitLine.Height = bottom - top;

        Canvas.SetLeft(Handle, Math.Clamp(x, left, right) - Handle.Width / 2);
        Canvas.SetTop(Handle, top + (bottom - top) / 2 - Handle.Height / 2);

        Canvas.SetLeft(BeforeBadge, left + 12);
        Canvas.SetTop(BeforeBadge, top + 12);
        BeforeBadge.Opacity = x - left > BadgeMinRoom ? 1 : 0;

        AfterBadge.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        Canvas.SetLeft(AfterBadge, right - AfterBadge.DesiredSize.Width - 12);
        Canvas.SetTop(AfterBadge, top + 12);
        AfterBadge.Opacity = right - x > BadgeMinRoom ? 1 : 0;
    }

    /// <summary>
    /// Fitted (the image shrinks to the window, nothing scrolls) or 100 % (one image pixel per screen pixel,
    /// whatever the Windows display scaling, scrollable and centered on the divider).
    /// </summary>
    private void ApplyZoom()
    {
        if (!_loaded) return;
        var actual = ActualSizeButton.IsChecked == true;
        var scale = actual ? Content.XamlRoot.RasterizationScale : 1;
        ImageHost.Width = _pixelWidth / scale;
        ImageHost.Height = _pixelHeight / scale;
        ImageBox.Stretch = actual ? Stretch.None : Stretch.Uniform;
        var mode = actual ? ScrollMode.Enabled : ScrollMode.Disabled;
        var bars = actual ? ScrollBarVisibility.Auto : ScrollBarVisibility.Disabled;
        Scroller.HorizontalScrollMode = Scroller.VerticalScrollMode = mode;
        Scroller.HorizontalScrollBarVisibility = Scroller.VerticalScrollBarVisibility = bars;

        Scroller.UpdateLayout();
        if (actual)
        {
            // Look where the divider is, half way down: that's where the difference is being compared.
            var margin = ImageBox.Margin.Left;
            Scroller.ChangeView(
                Math.Max(0, margin + _ratio * ImageHost.Width - Scroller.ViewportWidth / 2),
                Math.Max(0, margin + ImageHost.Height / 2 - Scroller.ViewportHeight / 2),
                null, disableAnimation: true);
        }
        else
        {
            Scroller.ChangeView(0, 0, null, disableAnimation: true);
        }
        UpdateSplit();
    }

    private void OnActualSizeToggled(object sender, RoutedEventArgs e) => ApplyZoom();

    private void OnZoomKeyInvoked(KeyboardAccelerator sender, KeyboardAcceleratorInvokedEventArgs args)
    {
        if (!ActualSizeButton.IsEnabled) return;
        args.Handled = true;
        ActualSizeButton.IsChecked = ActualSizeButton.IsChecked != true;
    }

    private void OnScrollerViewChanged(object? sender, ScrollViewerViewChangedEventArgs e) => UpdateSplit();

    /// <summary>A press on a scrollbar scrolls; it doesn't move the divider.</summary>
    private static bool IsOnScrollBar(object? source)
    {
        for (var element = source as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
        {
            if (element is Microsoft.UI.Xaml.Controls.Primitives.ScrollBar) return true;
        }
        return false;
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
        if (!_loaded || IsOnScrollBar(e.OriginalSource)) return;
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
