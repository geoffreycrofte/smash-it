using Windows.Graphics.Imaging;

namespace SmashIt.Core.Imaging;

/// <summary>Scales a JPEG down and re-encodes it, keeping its colors as they are (no color management).</summary>
internal static class JpegResampler
{
    /// <summary>Writes <paramref name="output"/>, a <paramref name="width"/>×<paramref name="height"/> JPEG.</summary>
    /// <param name="grayscale">One channel in, one channel out (a PDF's DeviceGray must stay one channel).</param>
    public static async Task ResizeAsync(
        string input, string output, uint width, uint height, bool grayscale, int quality, CancellationToken cancellationToken)
    {
        byte[] pixels;
        double dpiX, dpiY;
        await using (var file = new FileStream(input, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            using var stream = file.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(BitmapDecoder.JpegDecoderId, stream).AsTask(cancellationToken);
            var transform = new BitmapTransform
            {
                ScaledWidth = width,
                ScaledHeight = height,
                InterpolationMode = BitmapInterpolationMode.Fant, // area averaging: no aliasing when shrinking
            };
            var data = await decoder.GetPixelDataAsync(
                    grayscale ? BitmapPixelFormat.Gray8 : BitmapPixelFormat.Bgra8,
                    BitmapAlphaMode.Ignore,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage)
                .AsTask(cancellationToken);
            pixels = data.DetachPixelData();
            (dpiX, dpiY) = (decoder.DpiX, decoder.DpiY);
        }

        await using var target = new FileStream(output, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
        using var outStream = target.AsRandomAccessStream();
        var options = new BitmapPropertySet
        {
            ["ImageQuality"] = new BitmapTypedValue(Math.Clamp(quality, 1, 100) / 100f, Windows.Foundation.PropertyType.Single),
        };
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream, options).AsTask(cancellationToken);
        encoder.SetPixelData(
            grayscale ? BitmapPixelFormat.Gray8 : BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, dpiX, dpiY, pixels);
        await encoder.FlushAsync().AsTask(cancellationToken);
    }
}
