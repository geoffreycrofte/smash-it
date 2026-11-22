using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Storage;
using Windows.Storage.FileProperties;

namespace SmashIt.App.Services;

/// <summary>Row thumbnails from the Windows shell (the same ones File Explorer shows).</summary>
internal static class Thumbnails
{
    private const uint RequestedSize = 96; // 40 px row, sharp up to 200 % scaling

    /// <summary>
    /// Must be called on the UI thread. <c>null</c> keeps the kind icon: the shell only has a generic file icon
    /// (e.g. a PDF without a preview handler) or the file cannot be read.
    /// </summary>
    public static async Task<object?> LoadAsync(string path)
    {
        try
        {
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var thumbnail = await file.GetThumbnailAsync(
                ThumbnailMode.SingleItem, RequestedSize, ThumbnailOptions.ResizeThumbnail);
            if (thumbnail is null || thumbnail.Type != ThumbnailType.Image) return null;

            var bitmap = new BitmapImage { DecodePixelWidth = (int)RequestedSize, DecodePixelType = DecodePixelType.Physical };
            await bitmap.SetSourceAsync(thumbnail);
            return bitmap;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }

    /// <summary>For <c>x:Bind</c>: the row's thumbnail as an image source (the view-model holds it as an object).</summary>
    public static ImageSource? AsImage(object? thumbnail) => thumbnail as ImageSource;
}
