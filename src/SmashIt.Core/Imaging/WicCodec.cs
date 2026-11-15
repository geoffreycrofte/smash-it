using Windows.Graphics.Imaging;

namespace SmashIt.Core.Imaging;

/// <summary>Thin wrapper over the Windows Imaging Component (Windows.Graphics.Imaging).</summary>
internal static class WicCodec
{
    public sealed record DecodedImage(
        byte[] Pixels,
        uint Width,
        uint Height,
        double DpiX,
        double DpiY,
        BitmapPixelFormat SourcePixelFormat,
        uint FrameCount);

    /// <summary>Decodes the first frame as BGRA8.</summary>
    /// <param name="applyOrientation">Rotate/flip pixels according to the EXIF orientation.</param>
    /// <param name="convertToSrgb">Convert colors from the embedded profile to sRGB.</param>
    public static async Task<DecodedImage> DecodeAsync(
        string path,
        bool applyOrientation,
        bool convertToSrgb,
        BitmapAlphaMode alphaMode,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);

        var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                alphaMode,
                new BitmapTransform(),
                applyOrientation ? ExifOrientationMode.RespectExifOrientation : ExifOrientationMode.IgnoreExifOrientation,
                convertToSrgb ? ColorManagementMode.ColorManageToSRgb : ColorManagementMode.DoNotColorManage)
            .AsTask(cancellationToken);

        return new DecodedImage(
            pixelData.DetachPixelData(),
            applyOrientation ? decoder.OrientedPixelWidth : decoder.PixelWidth,
            applyOrientation ? decoder.OrientedPixelHeight : decoder.PixelHeight,
            decoder.DpiX,
            decoder.DpiY,
            decoder.BitmapPixelFormat,
            decoder.FrameCount);
    }

    public static async Task EncodeAsync(
        string path,
        Guid encoderId,
        BitmapPropertySet? encoderOptions,
        DecodedImage image,
        BitmapAlphaMode alphaMode,
        CancellationToken cancellationToken)
    {
        await using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var stream = file.AsRandomAccessStream();
        var encoder = encoderOptions is { Count: > 0 }
            ? await BitmapEncoder.CreateAsync(encoderId, stream, encoderOptions).AsTask(cancellationToken)
            : await BitmapEncoder.CreateAsync(encoderId, stream).AsTask(cancellationToken);

        encoder.SetPixelData(BitmapPixelFormat.Bgra8, alphaMode, image.Width, image.Height, image.DpiX, image.DpiY, image.Pixels);
        await encoder.FlushAsync().AsTask(cancellationToken);
    }
}
