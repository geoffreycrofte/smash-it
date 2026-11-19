using Microsoft.UI.Windowing;
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
    private bool _replaced;

    public MainViewModel ViewModel { get; }

    public MainWindow(MainViewModel viewModel)
    {
        ViewModel = viewModel;
        InitializeComponent();

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);
        AppWindow.Resize(new SizeInt32(1040, 720));
        CenterOnScreen();
        AppIcon.Apply(AppWindow, TitleBarIcon);
        ViewModel.AskWhereToSave = AskWhereToSaveAsync;

        PreviewCache.Clean(includeCurrent: false);
        // The preview copies belong to the files in the list: keep them when the window is only rebuilt.
        Closed += (_, _) =>
        {
            if (_replaced) return;
            ViewModel.FlushSettings();
            PreviewCache.Clean(includeCurrent: true);
        };
    }

    /// <summary>Shows the window on top, restored if it was minimized (files arrived from Explorer).</summary>
    public void BringToFront()
    {
        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter { State: Microsoft.UI.Windowing.OverlappedPresenterState.Minimized } presenter)
            presenter.Restore();
        Activate();
        SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));
    }

    [System.Runtime.InteropServices.LibraryImport("user32.dll")]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static partial bool SetForegroundWindow(nint window);

    /// <summary>Closes this window after a new one took its place (language change).</summary>
    public void CloseForReplacement()
    {
        _replaced = true;
        Close();
    }

    /// <summary>Opens where the splash screen was: the middle of the screen.</summary>
    private void CenterOnScreen()
    {
        var area = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Nearest).WorkArea;
        var size = AppWindow.Size;
        AppWindow.Move(new PointInt32(area.X + (area.Width - size.Width) / 2, area.Y + (area.Height - size.Height) / 2));
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
        e.DragUIOverride.Caption = Strings.DropCaption;
    }

    private async void OnDrop(object sender, DragEventArgs e)
    {
        if (!e.DataView.Contains(StandardDataFormats.StorageItems)) return;
        var items = await e.DataView.GetStorageItemsAsync();
        await ViewModel.AddPathsAsync(items.Select(i => i.Path));
    }

    /// <summary>"Where should the compressed files go?", preset to the last choice.</summary>
    private async Task<OutputLocationAnswer?> AskWhereToSaveAsync(IReadOnlyList<string> files)
    {
        BringToFront();
        var choice = new RadioButtons
        {
            Items = { Strings.NextToOriginals, Strings.InCompressedFolder },
            SelectedIndex = ViewModel.OutputLocationIndex,
        };
        var dontAsk = new CheckBox { Content = Strings.DontAskAgain };
        var dialog = new ContentDialog
        {
            XamlRoot = Content.XamlRoot,
            Title = Strings.WhereTitle,
            Content = new StackPanel
            {
                Spacing = 12,
                Children = { new TextBlock { Text = Strings.WhereIntro(files.Count), TextWrapping = TextWrapping.Wrap }, choice, dontAsk },
            },
            PrimaryButtonText = Strings.CompressButton,
            CloseButtonText = Strings.Cancel,
            DefaultButton = ContentDialogButton.Primary,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
        return new OutputLocationAnswer(choice.SelectedIndex == 1, dontAsk.IsChecked == true);
    }

    private async void OnAddFilesClick(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.PicturesLibrary };
        picker.FileTypeFilter.Add("*");

        // Unpackaged apps must tell the WinRT picker which window owns it.
        WinRT.Interop.InitializeWithWindow.Initialize(picker, WinRT.Interop.WindowNative.GetWindowHandle(this));

        IReadOnlyList<StorageFile> files = await picker.PickMultipleFilesAsync();
        await ViewModel.AddPathsAsync(files.Select(f => f.Path));
    }
}
