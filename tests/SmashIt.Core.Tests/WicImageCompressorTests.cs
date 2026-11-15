using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;
using Windows.Graphics.Imaging;

namespace SmashIt.Core.Tests;

public class WicImageCompressorTests
{
    private readonly WicImageCompressor _wic = new();

    [Theory]
    [InlineData(MediaFormat.Jpeg, MediaFormat.Jpeg, CompressionMode.Lossless, false)]
    [InlineData(MediaFormat.Jpeg, MediaFormat.Jpeg, CompressionMode.Lossy, true)]
    [InlineData(MediaFormat.Png, MediaFormat.Png, CompressionMode.Lossless, true)]
    [InlineData(MediaFormat.Png, MediaFormat.Jpeg, CompressionMode.Lossless, true)]
    [InlineData(MediaFormat.Png, MediaFormat.WebP, CompressionMode.Lossy, false)]
    [InlineData(MediaFormat.Pdf, MediaFormat.Pdf, CompressionMode.Lossy, false)]
    public void Declares_what_it_can_handle(MediaFormat input, MediaFormat output, CompressionMode mode, bool expected) =>
        Assert.Equal(expected, _wic.CanHandle(input, output, new CompressionOptions { Mode = mode }));

    [Fact]
    public async Task Converts_a_png_to_a_smaller_jpeg()
    {
        using var dir = new TempDirectory();
        var png = dir.File("photo.png");
        await WriteGradientPngAsync(png, 256, 256);
        var jpeg = dir.File("photo.jpg");

        await _wic.CompressAsync(png, jpeg, MediaFormat.Jpeg,
            new CompressionOptions { Mode = CompressionMode.Lossy, Quality = 70 },
            TestContext.Current.CancellationToken);

        Assert.Equal(MediaFormat.Jpeg, FormatDetector.DetectFile(jpeg));
        Assert.True(new FileInfo(jpeg).Length < new FileInfo(png).Length);
    }

    [Fact]
    public async Task Lower_jpeg_quality_gives_a_smaller_file()
    {
        using var dir = new TempDirectory();
        var png = dir.File("photo.png");
        await WriteGradientPngAsync(png, 256, 256);
        var high = dir.File("high.jpg");
        var low = dir.File("low.jpg");
        var ct = TestContext.Current.CancellationToken;

        await _wic.CompressAsync(png, high, MediaFormat.Jpeg, new CompressionOptions { Mode = CompressionMode.Lossy, Quality = 95 }, ct);
        await _wic.CompressAsync(png, low, MediaFormat.Jpeg, new CompressionOptions { Mode = CompressionMode.Lossy, Quality = 30 }, ct);

        Assert.True(new FileInfo(low).Length < new FileInfo(high).Length);
    }

    private static async Task WriteGradientPngAsync(string path, uint width, uint height)
    {
        var pixels = new byte[width * height * 4];
        var random = new Random(42);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * (int)width + x) * 4;
            pixels[i] = (byte)x;
            pixels[i + 1] = (byte)y;
            pixels[i + 2] = (byte)random.Next(256);
            pixels[i + 3] = 255;
        }

        await using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight, width, height, 96, 96, pixels);
        await encoder.FlushAsync();
    }
}
