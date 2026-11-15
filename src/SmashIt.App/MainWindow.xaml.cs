using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using SmashIt.App.Services;
using SmashIt.App.ViewModels;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace SmashIt.App;

public sealed partial class MainWindow : Window
{
    public MainViewModel ViewModel { get; } = new();

    public MainWindow()
    {
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1040, 720));

        // Keep the settings button clear of the minimize/maximize/close buttons, whatever the DPI.
        Root.Loaded += (_, _) => FitCaptionButtons();
        AppWindow.Changed += (_, _) => FitCaptionButtons();

        PreviewCache.Clean(includeCurrent: false);
        Closed += (_, _) => PreviewCache.Clean(includeCurrent: true);
    }

    private void FitCaptionButtons()
    {
        if (Root.XamlRoot is not { RasterizationScale: > 0 } root) return;
        CaptionButtonsColumn.Width = new GridLength(AppWindow.TitleBar.RightInset / root.RasterizationScale + 4);
    }

    private void OnPreviewClick(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is FileItem item) OpenPreview(item);
    }

    private void OnFileDoubleTapped(object sender, DoubleTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is FileItem item) OpenPreview(item);
    }

    private static void OpenPreview(FileItem item)
    {
        if (!item.CanPreview) return;
        new PreviewWindow(item, item.PreviewOriginalPath!, item.Result!.OutputPath!).Activate();
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        e.AcceptedOperation = DataPackageOperation.Copy;
        e.DragUIOverride.Caption = "Compresser";
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        ViewModel.AddPaths(items.Select(i => i.Path));
    }

    private async void OnAddFilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");

        // Unpackaged apps must tell the WinRT picker which window owns it.
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        ViewModel.AddPaths(files.Select(f => f.Path));
    }
}
