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
}
