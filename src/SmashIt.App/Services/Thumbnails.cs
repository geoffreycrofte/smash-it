using Microsoft.UI.Xaml.Media.Imaging;
using SmashIt.App.ViewModels;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace SmashIt.App.Services;

/// <summary>Row thumbnails from the Windows shell (the same ones File Explorer shows).</summary>
internal static class Thumbnails
{
    private const uint RequestedSize = 96; // 40 px row, sharp up to 200 % scaling

    /// <summary>
    /// Must be called on the UI thread. Keeps the kind icon when the shell only has a generic file icon
    /// (e.g. a PDF without a preview handler) or the file cannot be read.
    /// </summary>
    public static async Task LoadAsync(FileItem item)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(item.Path);
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem, RequestedSize, ThumbnailOptions.ResizeThumbnail);
            if (thumbnail is null || thumbnail.Type != ThumbnailType.Image) return;

            var bitmap = new BitmapImage { DecodePixelWidth = (int)RequestedSize, DecodePixelType = DecodePixelType.Physical };
            await bitmap.SetSourceAsync(thumbnail);
            item.Thumbnail = bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or System.Runtime.InteropServices.COMException)
        {
            // No thumbnail: the kind icon stays.
        }
    }
}
