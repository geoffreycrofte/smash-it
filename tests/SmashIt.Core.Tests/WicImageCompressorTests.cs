using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;

namespace SmashIt.Core.Tests;

public class WicImageCompressorTests
{
    private readonly WicImageCompressor _wic = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

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
        var png = await TestImages.WriteAsync(dir.File("photo.png"), MediaFormat.Png, 256, 256);
        var jpeg = dir.File("photo.jpg");

        await _wic.CompressAsync(Job(png, MediaFormat.Png, jpeg, MediaFormat.Jpeg, 70), Ct);

        Assert.Equal(MediaFormat.Jpeg, FormatDetector.DetectFile(jpeg));
        Assert.True(new FileInfo(jpeg).Length < new FileInfo(png).Length);
    }

    [Fact]
    public async Task Lower_jpeg_quality_gives_a_smaller_file()
    {
        using var dir = new TempDirectory();
        var png = await TestImages.WriteAsync(dir.File("photo.png"), MediaFormat.Png, 256, 256);
        var high = dir.File("high.jpg");
        var low = dir.File("low.jpg");

        await _wic.CompressAsync(Job(png, MediaFormat.Png, high, MediaFormat.Jpeg, 95), Ct);
        await _wic.CompressAsync(Job(png, MediaFormat.Png, low, MediaFormat.Jpeg, 30), Ct);

        Assert.True(new FileInfo(low).Length < new FileInfo(high).Length);
    }

    private static CompressionJob Job(string input, MediaFormat inputFormat, string output, MediaFormat outputFormat, int quality) =>
        new(input, inputFormat, output, outputFormat, new CompressionOptions { Mode = CompressionMode.Lossy, Quality = quality });
}
