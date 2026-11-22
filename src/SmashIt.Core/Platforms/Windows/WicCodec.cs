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
    /// <param name="scale">Scales both dimensions down (0–1) with area averaging; <c>null</c> keeps the size.</param>
    public static async Task<DecodedImage> DecodeAsync(
        string path,
        bool applyOrientation,
        bool convertToSrgb,
        BitmapAlphaMode alphaMode,
        CancellationToken cancellationToken,
        double? scale = null)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);

        // WIC scales the stored frame, then rotates it: the scaled size is that of the stored frame.
        var (width, height) = ScaledSize(decoder.PixelWidth, decoder.PixelHeight, scale);
        var transform = new BitmapTransform();
        if (scale is not null)
        {
            transform.ScaledWidth = width;
            transform.ScaledHeight = height;
            transform.InterpolationMode = BitmapInterpolationMode.Fant; // no aliasing when shrinking
        }

        var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                alphaMode,
                transform,
                applyOrientation ? ExifOrientationMode.RespectExifOrientation : ExifOrientationMode.IgnoreExifOrientation,
                convertToSrgb ? ColorManagementMode.ColorManageToSRgb : ColorManagementMode.DoNotColorManage)
            .AsTask(cancellationToken);

        // A 90° orientation swaps the dimensions of what comes out.
        var swapped = applyOrientation && decoder.OrientedPixelWidth != decoder.PixelWidth;
        return new DecodedImage(
            pixelData.DetachPixelData(),
            swapped ? height : width,
            swapped ? width : height,
            decoder.DpiX,
            decoder.DpiY,
            decoder.BitmapPixelFormat,
            decoder.FrameCount);
    }

    /// <summary>The pixel size of an image, or <c>null</c> when Windows can't read it.</summary>
    public static async Task<(uint Width, uint Height)?> ReadSizeAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var stream = file.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
            return (decoder.PixelWidth, decoder.PixelHeight);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>The number of frames (several for an animated GIF), or 0 when Windows can't read the image.</summary>
    public static async Task<uint> ReadFrameCountAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var stream = file.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(cancellationToken);
            return decoder.FrameCount;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            return 0;
        }
    }

    public static (uint Width, uint Height) ScaledSize(uint width, uint height, double? scale) =>
        scale is { } s
            ? ((uint)Math.Max(1, Math.Round(width * s)), (uint)Math.Max(1, Math.Round(height * s)))
            : (width, height);

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
