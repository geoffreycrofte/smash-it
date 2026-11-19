using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;
using SmashIt.Core.Metadata;

namespace SmashIt.Core.Tests;

/// <summary>Runs the bundled tools for real. Skipped when tools\fetch-tools.ps1 has not been run.</summary>
public class ToolCompressorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static T Require<T>(T compressor) where T : ICompressor
    {
        if (!compressor.IsAvailable) Assert.Skip($"{compressor.DisplayName} absent : lancez tools\\fetch-tools.ps1.");
        return compressor;
    }

    [Fact]
    public async Task Oxipng_shrinks_a_wic_png_without_changing_pixels()
    {
        var oxipng = Require(new OxipngCompressor());
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("a.png"), MediaFormat.Png, 200, 100);
        var before = new FileInfo(path).Length;

        var result = await new CompressionPipeline([oxipng])
            .ProcessAsync(path, new CompressionOptions { OriginalHandling = OriginalHandling.Overwrite }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.True(new FileInfo(path).Length < before);
        Assert.Equal((200u, 100u), await TestImages.SizeAsync(path));
    }

    [Fact]
    public async Task Jpegtran_optimizes_losslessly_and_keeps_the_metadata_policy()
    {
        var jpegtran = Require(new JpegtranCompressor());
        using var dir = new TempDirectory();
        var metadata = new ImageMetadata { SourceFormat = MediaFormat.Jpeg, Exif = TestImages.Exif(6), Icc = TestImages.FakeIcc };
        var path = await TestImages.WriteAsync(dir.File("a.jpg"), MediaFormat.Jpeg, 300, 200, metadata);

        var result = await new CompressionPipeline([jpegtran])
            .ProcessAsync(path, new CompressionOptions { OriginalHandling = OriginalHandling.Overwrite }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        var after = MetadataTransfer.Read(path, MediaFormat.Jpeg)!;
        Assert.Equal(TestImages.FakeIcc, after.Icc);
        Assert.Equal(ExifOrientation.CreateMinimal(6), after.Exif);
    }

    [Theory]
    [InlineData(CompressionMode.Lossy)]
    [InlineData(CompressionMode.Lossless)]
    public async Task Cwebp_converts_to_webp(CompressionMode mode)
    {
        var cwebp = Require(new CwebpCompressor());
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("a.png"), MediaFormat.Png, 120, 80);

        var result = await new CompressionPipeline([cwebp]).ProcessAsync(path, new CompressionOptions
        {
            Mode = mode, TargetFormat = MediaFormat.WebP, OriginalHandling = OriginalHandling.KeepAlongside,
        }, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        Assert.Equal(dir.File("a-compressed.webp"), result.OutputPath);
        Assert.Equal(MediaFormat.WebP, FormatDetector.DetectFile(result.OutputPath!));
    }

    [Fact]
    public async Task Avifenc_converts_to_avif()
    {
        var avifenc = Require(new AvifencCompressor());
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("a.jpg"), MediaFormat.Jpeg, 120, 80);

        var result = await new CompressionPipeline([avifenc]).ProcessAsync(path, new CompressionOptions
        {
            Mode = CompressionMode.Lossy, Quality = 60, TargetFormat = MediaFormat.Avif,
            OriginalHandling = OriginalHandling.KeepAlongside,
        }, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        Assert.Equal(MediaFormat.Avif, FormatDetector.DetectFile(result.OutputPath!));
    }

    private static readonly CompressionOptions LossyPng = new()
    {
        Mode = CompressionMode.Lossy, Quality = 85, OriginalHandling = OriginalHandling.Overwrite,
    };

    /// <summary>PNG color type from IHDR: 3 is a palette, 2/6 are truecolor.</summary>
    private static byte PngColorType(string path) => File.ReadAllBytes(path)[25];

    private static IEnumerable<string> PngChunkTypes(string path)
    {
        var bytes = File.ReadAllBytes(path);
        for (var pos = 8; pos + 8 <= bytes.Length;)
        {
            var length = (bytes[pos] << 24) | (bytes[pos + 1] << 16) | (bytes[pos + 2] << 8) | bytes[pos + 3];
            yield return System.Text.Encoding.ASCII.GetString(bytes, pos + 4, 4);
            pos += 12 + length;
        }
    }

    [Fact]
    public async Task Pngquant_turns_a_truecolor_png_into_a_smaller_palette_png()
    {
        var pngquant = Require(new PngquantCompressor());
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("a.png"), MediaFormat.Png, 320, 200, noisy: false);
        var before = new FileInfo(path).Length;

        var result = await new CompressionPipeline([pngquant]).ProcessAsync(path, LossyPng, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.Equal("pngquant", result.CompressorId);
        Assert.True(new FileInfo(path).Length < before);
        Assert.Equal(3, PngColorType(path));
        Assert.Equal((320u, 200u), await TestImages.SizeAsync(path));
    }

    [Fact]
    public async Task Pngquant_keeps_the_source_color_profile_instead_of_converting_to_srgb()
    {
        var pngquant = Require(new PngquantCompressor());
        using var dir = new TempDirectory();
        var metadata = new ImageMetadata { SourceFormat = MediaFormat.Png, Icc = TestImages.FakeIcc };
        var path = await TestImages.WriteAsync(dir.File("a.png"), MediaFormat.Png, 320, 200, metadata, noisy: false);

        var result = await new CompressionPipeline([pngquant]).ProcessAsync(path, LossyPng, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.Equal(TestImages.FakeIcc, MetadataTransfer.Read(path, MediaFormat.Png)!.Icc);
        var chunks = PngChunkTypes(path).ToList();
        Assert.DoesNotContain("sRGB", chunks); // sRGB and iCCP must not coexist
        Assert.DoesNotContain("gAMA", chunks);
    }

    [Fact]
    public async Task Pngquant_gives_way_to_lossless_engines_when_the_quality_floor_is_not_met()
    {
        var pngquant = Require(new PngquantCompressor());
        var oxipng = Require(new OxipngCompressor());
        using var dir = new TempDirectory();
        // Random noise can't fit in 256 colors at quality 100 (floor 70): pngquant writes nothing.
        var path = await TestImages.WriteAsync(dir.File("a.png"), MediaFormat.Png, 200, 100);

        var result = await new CompressionPipeline([pngquant, oxipng])
            .ProcessAsync(path, LossyPng with { Quality = 100 }, Ct);

        Assert.NotEqual(CompressionStatus.Failed, result.Status);
        Assert.DoesNotContain("pngquant", result.CompressorId ?? string.Empty);
        Assert.NotEqual(3, PngColorType(path));
    }

    [Fact]
    public void Pngquant_only_runs_in_lossy_mode_and_only_writes_png()
    {
        var pngquant = new PngquantCompressor();
        var lossy = new CompressionOptions { Mode = CompressionMode.Lossy };

        Assert.True(pngquant.CanHandle(MediaFormat.Png, MediaFormat.Png, lossy));
        Assert.True(pngquant.CanHandle(MediaFormat.Jpeg, MediaFormat.Png, lossy));
        Assert.False(pngquant.CanHandle(MediaFormat.Png, MediaFormat.Png, CompressionOptions.Default));
        Assert.False(pngquant.CanHandle(MediaFormat.Png, MediaFormat.WebP, lossy));
    }

    [Theory]
    [InlineData(85, 55, 85)]
    [InlineData(100, 70, 100)]
    [InlineData(20, 0, 20)]
    [InlineData(0, 0, 1)]
    public void Pngquant_quality_range_follows_the_quality_setting(int quality, int min, int max) =>
        Assert.Equal((min, max), PngquantCompressor.QualityRange(quality));
}
