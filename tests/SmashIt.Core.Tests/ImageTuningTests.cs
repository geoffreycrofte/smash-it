using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Tests;

/// <summary>Each fine-tuning setting reaches the file the engine writes.</summary>
public class ImageTuningTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CompressionOptions Lossy = new()
    {
        Mode = CompressionMode.Lossy, Quality = 80, OriginalHandling = OriginalHandling.KeepAlongside,
    };

    private static T Require<T>(T engine) where T : ICompressor
    {
        if (!engine.IsAvailable) Assert.Skip($"{engine.DisplayName} absent : lancez tools\\fetch-tools.ps1.");
        return engine;
    }

    [Theory]
    [InlineData(80, ColorDetail.Automatic, 0x22)] // 4:2:0 below quality 90
    [InlineData(95, ColorDetail.Automatic, 0x11)] // 4:4:4 above
    [InlineData(80, ColorDetail.Full, 0x11)]
    [InlineData(95, ColorDetail.Reduced, 0x22)]
    public async Task Color_detail_sets_the_jpeg_subsampling(int quality, ColorDetail detail, byte lumaSampling)
    {
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("photo.png"), MediaFormat.Png, 64, 48);
        var options = Lossy with { Quality = quality, TargetFormat = MediaFormat.Jpeg, Tuning = new() { ColorDetail = detail } };

        var result = await new CompressionPipeline([new WicImageCompressor()]).ProcessAsync(path, options, Ct);

        Assert.Equal(lumaSampling, LumaSampling(await File.ReadAllBytesAsync(result.OutputPath!, Ct)));
    }

    [Theory]
    [InlineData(JpegEncoding.Progressive, 0xC2)]
    [InlineData(JpegEncoding.Baseline, 0xC0)]
    public async Task Jpeg_encoding_decides_progressive_or_baseline(JpegEncoding encoding, byte frameMarker)
    {
        var jpegtran = Require(new JpegtranCompressor());
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("photo.jpg"), MediaFormat.Jpeg, 256, 192);
        var options = Lossy with { Mode = CompressionMode.Lossless, Tuning = new() { JpegEncoding = encoding } };
        var output = dir.File("out.jpg");

        await jpegtran.CompressAsync(new CompressionJob(path, MediaFormat.Jpeg, output, MediaFormat.Jpeg, options), Ct);

        Assert.Equal(frameMarker, FrameMarker(await File.ReadAllBytesAsync(output, Ct)));
    }

    [Fact]
    public void Pngquant_gets_the_color_count_and_dithering()
    {
        var auto = PngquantCompressor.Arguments(Lossy, "in.png", "out.png");
        var tuned = PngquantCompressor.Arguments(Lossy with { Tuning = new() { MaxColors = 16, Dithering = false } }, "in.png", "out.png");

        Assert.Contains("50-80", auto);
        Assert.DoesNotContain("--nofs", auto);
        Assert.Equal(["--", "in.png"], auto[^2..]);
        Assert.Contains("0-80", tuned); // the chosen palette wins over the usual quality floor
        Assert.Contains("--nofs", tuned);
        Assert.Equal(["16", "--", "in.png"], tuned[^3..]);
    }

    [Fact]
    public async Task Maximum_colors_limits_the_png_palette()
    {
        var pngquant = Require(new PngquantCompressor());
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("photo.png"), MediaFormat.Png, 128, 96);

        var result = await new CompressionPipeline([pngquant]).ProcessAsync(path, Lossy with { Tuning = new() { MaxColors = 16 } }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        var png = await File.ReadAllBytesAsync(result.OutputPath!, Ct);
        var plte = png.AsSpan().IndexOf("PLTE"u8);
        Assert.True(plte > 4, "no palette");
        var length = (png[plte - 4] << 24) | (png[plte - 3] << 16) | (png[plte - 2] << 8) | png[plte - 1];
        Assert.InRange(length / 3, 2, 16);
    }

    [Fact]
    public async Task Maximum_colors_limits_the_gif_color_table()
    {
        var gifsicle = Require(new GifsicleCompressor());
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("anim.gif"), MediaFormat.Gif, 128, 96);

        async Task<int> ColorTable(string gif)
        {
            var info = (await ExternalTool.RunRawAsync(ExternalTool.Find("gifsicle")!, ["--info", gif], Ct)).StandardOutput;
            return int.Parse(System.Text.RegularExpressions.Regex.Match(info, @"color table \[(\d+)\]").Groups[1].Value);
        }
        Assert.True(await ColorTable(path) > 16, "the test image should have more colors to begin with");

        var result = await new CompressionPipeline([gifsicle]).ProcessAsync(path, Lossy with { Tuning = new() { MaxColors = 16 } }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.InRange(await ColorTable(result.OutputPath!), 2, 16);
    }

    [Theory]
    [InlineData(ColorDetail.Full, 0, 0)]
    [InlineData(ColorDetail.Reduced, 1, 1)]
    public async Task Color_detail_sets_the_avif_subsampling(ColorDetail detail, int x, int y)
    {
        var avifenc = Require(new AvifencCompressor());
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("photo.png"), MediaFormat.Png, 64, 48);
        var options = Lossy with { TargetFormat = MediaFormat.Avif, Tuning = new() { ColorDetail = detail } };

        var result = await new CompressionPipeline([avifenc]).ProcessAsync(path, options, Ct);

        var avif = await File.ReadAllBytesAsync(result.OutputPath!, Ct);
        // av1C: marker/version, profile/level, then tier, high bit depth, 12-bit, mono, subsampling x, subsampling y…
        var flags = avif[avif.AsSpan().IndexOf("av1C"u8) + 6];
        Assert.Equal((x, y), ((flags >> 3) & 1, (flags >> 2) & 1));
    }

    [Fact]
    public async Task Zopfli_gives_a_valid_png_no_larger()
    {
        var oxipng = Require(new OxipngCompressor());
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("shot.png"), MediaFormat.Png, 96, 64, noisy: false);
        var pipeline = new CompressionPipeline([oxipng]);
        var lossless = Lossy with { Mode = CompressionMode.Lossless, KeptOriginalSuffix = "-plain" };

        var plain = await pipeline.ProcessAsync(path, lossless, Ct);
        var zopfli = await pipeline.ProcessAsync(path, lossless with { KeptOriginalSuffix = "-zopfli", Tuning = new() { Zopfli = true } }, Ct);

        Assert.Equal(CompressionStatus.Optimized, zopfli.Status);
        Assert.Equal((96u, 64u), await TestImages.SizeAsync(zopfli.OutputPath!));
        Assert.True(zopfli.FinalSize <= plain.FinalSize, $"{zopfli.FinalSize} > {plain.FinalSize}");
    }

    /// <summary>The luma component's sampling factors in the JPEG frame header (0x22 = 4:2:0, 0x11 = 4:4:4).</summary>
    private static byte LumaSampling(byte[] jpeg)
    {
        var sof = FrameMarkerIndex(jpeg);
        return jpeg[sof + 9]; // after length (2), precision (1), height (2), width (2), count (1), component id (1)
    }

    private static byte FrameMarker(byte[] jpeg) => jpeg[FrameMarkerIndex(jpeg) - 1];

    private static int FrameMarkerIndex(byte[] jpeg)
    {
        for (var i = 2; i + 1 < jpeg.Length; i++)
            if (jpeg[i] == 0xFF && jpeg[i + 1] is 0xC0 or 0xC1 or 0xC2) return i + 2;
        throw new InvalidDataException("No JPEG frame header.");
    }
}
