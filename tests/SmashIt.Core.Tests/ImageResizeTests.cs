using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Tests;

/// <summary>Lossy images larger than the size limit are scaled down; nothing else is.</summary>
public class ImageResizeTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CompressionOptions Lossy200 = new()
    {
        Mode = CompressionMode.Lossy, Quality = 80, ImageMaxSide = 200, OriginalHandling = OriginalHandling.KeepAlongside,
    };

    [Theory]
    [InlineData(400u, 300u, 200, 0.5)]
    [InlineData(300u, 400u, 200, 0.5)]    // portrait: the long side counts
    [InlineData(200u, 150u, 200, null)]   // already at the limit
    [InlineData(4000u, 3000u, 0, null)]   // no limit
    public void The_long_side_decides(uint w, uint h, int max, double? scale) =>
        Assert.Equal(scale, CompressionPipeline.ScaleFor((w, h), max));

    [Fact]
    public async Task A_jpeg_larger_than_the_limit_is_scaled_down_and_optimizers_only_polish_it()
    {
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("photo.jpg"), MediaFormat.Jpeg, 400, 300, noisy: false);
        var engines = new List<ICompressor> { new WicImageCompressor() };
        if (new JpegtranCompressor() is { IsAvailable: true } jpegtran) engines.Add(jpegtran);

        var result = await new CompressionPipeline(engines).ProcessAsync(path, Lossy200, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.Equal((200u, 150u), await TestImages.SizeAsync(result.OutputPath!));
        Assert.StartsWith("wic", result.CompressorId); // jpegtran can't scale: it may only follow
    }

    [Fact]
    public async Task Lossless_mode_and_small_images_keep_their_size()
    {
        using var dir = new TempDirectory();
        var big = await TestImages.WriteAsync(dir.File("big.png"), MediaFormat.Png, 400, 300);
        var small = await TestImages.WriteAsync(dir.File("small.jpg"), MediaFormat.Jpeg, 160, 120, noisy: false);
        var pipeline = new CompressionPipeline([new WicImageCompressor()]);

        var lossless = await pipeline.ProcessAsync(big, Lossy200 with { Mode = CompressionMode.Lossless }, Ct);
        var smallResult = await pipeline.ProcessAsync(small, Lossy200, Ct);

        Assert.Equal((400u, 300u), await TestImages.SizeAsync(lossless.OutputPath ?? big));
        Assert.Equal((160u, 120u), await TestImages.SizeAsync(smallResult.OutputPath ?? small));
    }

    [Fact]
    public async Task A_conversion_to_webp_is_scaled_too()
    {
        var cwebp = new CwebpCompressor();
        if (!cwebp.IsAvailable) Assert.Skip("cwebp absent : lancez tools\\fetch-tools.ps1.");
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("photo.jpg"), MediaFormat.Jpeg, 400, 300, noisy: false);

        var result = await new CompressionPipeline([cwebp]).ProcessAsync(path, Lossy200 with { TargetFormat = MediaFormat.WebP }, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        var bytes = await File.ReadAllBytesAsync(result.OutputPath!, Ct);
        // VP8 frame header: 14-bit width and height, little-endian, right after the start code.
        var vp8 = bytes.AsSpan().IndexOf("VP8 "u8) + 8;
        Assert.Equal(200, BitConverter.ToUInt16(bytes, vp8 + 6) & 0x3FFF);
        Assert.Equal(150, BitConverter.ToUInt16(bytes, vp8 + 8) & 0x3FFF);
    }

    [Fact]
    public async Task A_png_through_pngquant_is_scaled_down()
    {
        var pngquant = new PngquantCompressor();
        if (!pngquant.IsAvailable) Assert.Skip("pngquant absent : lancez tools\\fetch-tools.ps1.");
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("shot.png"), MediaFormat.Png, 400, 300, noisy: false);

        var result = await new CompressionPipeline([pngquant]).ProcessAsync(path, Lossy200 with { Quality = 90 }, Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal((200u, 150u), await TestImages.SizeAsync(result.OutputPath ?? path));
    }

    [Fact]
    public async Task An_animated_gif_is_scaled_down_with_all_its_frames()
    {
        var gifsicle = new GifsicleCompressor();
        if (!gifsicle.IsAvailable) Assert.Skip("gifsicle absent : lancez tools\\fetch-tools.ps1.");
        using var dir = new TempDirectory();
        var frames = new[]
        {
            await TestImages.WriteAsync(dir.File("f1.gif"), MediaFormat.Gif, 400, 300, noisy: false),
            await TestImages.WriteAsync(dir.File("f2.gif"), MediaFormat.Gif, 400, 300, noisy: true),
        };
        var gif = dir.File("anim.gif");
        await ExternalTool.RunAsync(ExternalTool.Find("gifsicle.exe")!, ["--merge", "--loopcount=forever", .. frames, "--output", gif], Ct);

        var result = await new CompressionPipeline([gifsicle]).ProcessAsync(gif, Lossy200, Ct);

        Assert.True(result.IsSuccess);
        var info = (await ExternalTool.RunRawAsync(ExternalTool.Find("gifsicle.exe")!, ["--info", result.OutputPath ?? gif], Ct)).StandardOutput;
        Assert.Contains("2 images", info);
        Assert.Contains("logical screen 200x150", info);
    }
}
