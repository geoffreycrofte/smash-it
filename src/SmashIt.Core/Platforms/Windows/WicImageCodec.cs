using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;
using Windows.Graphics.Imaging;

namespace SmashIt.Core.Imaging;

/// <summary><see cref="IImageCodec"/> over the Windows Imaging Component.</summary>
internal sealed class WicImageCodec : IImageCodec
{
    public bool CanRead(MediaFormat format) => WicImageCompressor.CanRead(format);

    public Task<(uint Width, uint Height)?> ReadSizeAsync(string path, CancellationToken cancellationToken) =>
        WicCodec.ReadSizeAsync(path, cancellationToken);

    public Task<uint> ReadFrameCountAsync(string path, CancellationToken cancellationToken) =>
        WicCodec.ReadFrameCountAsync(path, cancellationToken);

    public async Task<uint> WritePngAsync(string input, string output, PngConversion conversion, CancellationToken cancellationToken)
    {
        var image = await WicCodec.DecodeAsync(
            input, conversion.ApplyOrientation, conversion.ConvertToSrgb, BitmapAlphaMode.Straight, cancellationToken, conversion.Scale);

        var options = conversion.Fast
            ? new BitmapPropertySet
            {
                // WICPngFilterNone: the fastest to encode.
                ["FilterOption"] = new BitmapTypedValue((byte)1, Windows.Foundation.PropertyType.UInt8),
            }
            : null;
        await WicCodec.EncodeAsync(output, BitmapEncoder.PngEncoderId, options, image, BitmapAlphaMode.Straight, cancellationToken);
        return image.FrameCount;
    }

    public async Task ResizeJpegAsync(
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
