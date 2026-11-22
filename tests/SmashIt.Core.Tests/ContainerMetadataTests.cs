using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;
using SmashIt.Core.Metadata;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Tests;

/// <summary>EXIF and XMP of WebP, HEIC and AVIF files, read and carried over to the result.</summary>
public class ContainerMetadataTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    // "Make" = "ABC" lets us check the EXIF that arrives is the one we wrote, not a minimal one.
    private static readonly byte[] Exif = TestImages.Exif(1);

    /// <summary>
    /// Skips the test when Windows can't decode this file: WebP and AVIF need Store extensions (WebP Image
    /// Extension, AV1 Video Extension) that some machines, GitHub's Windows Server runners among them, don't have.
    /// </summary>
    private static async Task RequireWindowsDecoder(string path)
    {
        try
        {
            await TestImages.SizeAsync(path);
        }
        catch (Exception)
        {
            Assert.Skip($"Windows can't decode {Path.GetExtension(path)} here (Store image extension missing).");
        }
    }

    private static string Tool(string name) =>
        ExternalTool.Find(name) ?? throw Xunit.Sdk.SkipException.ForSkip($"{name} absent : lancez tools\\fetch-tools.ps1.");

    /// <summary>A PNG carrying <see cref="Exif"/> and <see cref="TestImages.Xmp"/>.</summary>
    private static async Task<string> PngWithMetadataAsync(TempDirectory dir)
    {
        var metadata = new ImageMetadata { SourceFormat = MediaFormat.Png, Exif = Exif, Xmp = TestImages.Xmp };
        return await TestImages.WriteAsync(dir.File("source.png"), MediaFormat.Png, 160, 120, metadata, noisy: false);
    }

    /// <summary>An AVIF (HEIF container, like HEIC) made by avifenc, with EXIF and XMP items.</summary>
    private static async Task<string> AvifWithMetadataAsync(TempDirectory dir)
    {
        var avifenc = Tool("avifenc");
        var png = await TestImages.WriteAsync(dir.File("plain.png"), MediaFormat.Png, 160, 120, noisy: false);
        await File.WriteAllBytesAsync(dir.File("exif.bin"), Exif, Ct);
        await File.WriteAllBytesAsync(dir.File("xmp.xml"), TestImages.Xmp, Ct);
        var avif = dir.File("photo.avif");
        await ExternalTool.RunAsync(avifenc, ["--exif", dir.File("exif.bin"), "--xmp", dir.File("xmp.xml"), "-q", "80", png, avif], Ct);
        return avif;
    }

    /// <summary>
    /// A WebP made by Smash It from a PNG with metadata, keeping it. Also checks the PNG → WebP path itself: the
    /// Windows build of cwebp drops EXIF and XMP, which Smash It adds back.
    /// </summary>
    private static async Task<string> WebPWithMetadataAsync(TempDirectory dir)
    {
        Tool("cwebp");
        var png = await PngWithMetadataAsync(dir);
        var result = await new CompressionPipeline([new CwebpCompressor()]).ProcessAsync(png, new CompressionOptions
        {
            StripMetadata = false, Mode = CompressionMode.Lossy, TargetFormat = MediaFormat.WebP,
            OriginalHandling = OriginalHandling.KeepAlongside,
        }, Ct);
        Assert.Equal(CompressionStatus.Converted, result.Status);
        var webp = dir.File("photo.webp");
        File.Move(result.OutputPath!, webp);
        return webp;
    }

    [Theory]
    [InlineData(CompressionMode.Lossy)]    // simple VP8 → extended
    [InlineData(CompressionMode.Lossless)] // simple VP8L → extended
    public async Task Metadata_added_to_a_webp_keeps_the_image_readable(CompressionMode mode)
    {
        Tool("cwebp");
        using var dir = new TempDirectory();
        var png = await TestImages.WriteAsync(dir.File("plain.png"), MediaFormat.Png, 161, 97, noisy: false);
        var result = await new CompressionPipeline([new CwebpCompressor()]).ProcessAsync(png, new CompressionOptions
        {
            Mode = mode, TargetFormat = MediaFormat.WebP, OriginalHandling = OriginalHandling.KeepAlongside,
        }, Ct);
        var plain = await File.ReadAllBytesAsync(result.OutputPath!, Ct);

        await RequireWindowsDecoder(result.OutputPath!);
        var withMetadata = ContainerMetadataIO.WriteWebP(plain, Exif, TestImages.Xmp);
        await File.WriteAllBytesAsync(dir.File("with.webp"), withMetadata, Ct);

        Assert.Equal((161u, 97u), await TestImages.SizeAsync(dir.File("with.webp"))); // Windows still decodes it
        var read = ContainerMetadataIO.Read(withMetadata, MediaFormat.WebP)!;
        Assert.Equal(Exif, read.Exif);
        Assert.Equal(TestImages.Xmp, read.Xmp);
    }

    [Fact]
    public async Task Reads_exif_and_xmp_from_a_heif_container()
    {
        using var dir = new TempDirectory();
        var avif = await AvifWithMetadataAsync(dir);

        var metadata = ContainerMetadataIO.Read(await File.ReadAllBytesAsync(avif, Ct), MediaFormat.Avif);

        Assert.NotNull(metadata);
        Assert.Equal(Exif, metadata.Exif);
        Assert.Equal(TestImages.Xmp, metadata.Xmp);
    }

    [Fact]
    public async Task Reads_exif_and_xmp_from_a_webp()
    {
        using var dir = new TempDirectory();
        var webp = await WebPWithMetadataAsync(dir);

        var metadata = ContainerMetadataIO.Read(await File.ReadAllBytesAsync(webp, Ct), MediaFormat.WebP);

        Assert.NotNull(metadata);
        Assert.Equal(Exif, metadata.Exif);
        Assert.Equal(TestImages.Xmp, metadata.Xmp);
    }

    [Fact]
    public void Broken_or_foreign_files_give_no_metadata()
    {
        Assert.Null(ContainerMetadataIO.Read("RIFF\x10\0\0\0WEBPVP8 "u8.ToArray(), MediaFormat.WebP));
        Assert.Null(ContainerMetadataIO.Read([0, 0, 0, 24, .. "ftypheic"u8, 0, 0, 0, 0], MediaFormat.Heif));
        Assert.Null(ContainerMetadataIO.Read("not an image at all"u8.ToArray(), MediaFormat.Avif));
    }

    private static readonly CompressionOptions KeepMetadata = new()
    {
        StripMetadata = false, OriginalHandling = OriginalHandling.KeepAlongside,
    };

    [Fact]
    public async Task A_webp_converted_to_jpeg_keeps_its_exif_and_xmp()
    {
        using var dir = new TempDirectory();
        var webp = await WebPWithMetadataAsync(dir);
        await RequireWindowsDecoder(webp);

        var result = await new CompressionPipeline([.. Platform.ImageEngines()]).ProcessAsync(webp, KeepMetadata with
        {
            Mode = CompressionMode.Lossy, TargetFormat = MediaFormat.Jpeg,
        }, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        var after = MetadataTransfer.Read(result.OutputPath!, MediaFormat.Jpeg)!;
        Assert.Equal(Exif, after.Exif);
        Assert.Equal(TestImages.Xmp, after.Xmp);
        Assert.Null(result.Message); // no "metadata can't be kept" note
    }

    [Fact]
    public async Task A_webp_recompressed_as_webp_keeps_its_exif_and_xmp()
    {
        using var dir = new TempDirectory();
        var webp = await WebPWithMetadataAsync(dir);
        await RequireWindowsDecoder(webp);

        var result = await new CompressionPipeline([new CwebpCompressor()]).ProcessAsync(webp, KeepMetadata with
        {
            Mode = CompressionMode.Lossy, Quality = 50,
        }, Ct);

        Assert.True(result.IsSuccess);
        var output = result.OutputPath ?? webp;
        var after = ContainerMetadataIO.Read(await File.ReadAllBytesAsync(output, Ct), MediaFormat.WebP);
        Assert.NotNull(after);
        Assert.Equal(Exif, after.Exif);
        Assert.Equal(TestImages.Xmp, after.Xmp);
    }

    [Fact]
    public async Task Stripping_metadata_still_removes_it()
    {
        using var dir = new TempDirectory();
        var webp = await WebPWithMetadataAsync(dir);
        await RequireWindowsDecoder(webp);

        var result = await new CompressionPipeline([.. Platform.ImageEngines()]).ProcessAsync(webp, KeepMetadata with
        {
            StripMetadata = true, Mode = CompressionMode.Lossy, TargetFormat = MediaFormat.Jpeg,
        }, Ct);

        var after = MetadataTransfer.Read(result.OutputPath!, MediaFormat.Jpeg);
        Assert.True(after is null || (after.Exif is null && after.Xmp is null));
    }

    [Fact]
    public async Task A_png_and_a_webp_converted_to_avif_keep_their_exif_and_xmp()
    {
        Tool("avifenc");
        using var dir = new TempDirectory();
        var webp = await WebPWithMetadataAsync(dir);
        await RequireWindowsDecoder(webp);
        var png = dir.File("source.png"); // made along with the WebP
        var options = KeepMetadata with { Mode = CompressionMode.Lossy, Quality = 60, TargetFormat = MediaFormat.Avif };

        foreach (var source in new[] { png, webp })
        {
            var result = await new CompressionPipeline([new AvifencCompressor()]).ProcessAsync(source, options, Ct);

            Assert.Equal(CompressionStatus.Converted, result.Status);
            var after = ContainerMetadataIO.Read(await File.ReadAllBytesAsync(result.OutputPath!, Ct), MediaFormat.Avif);
            Assert.NotNull(after);
            Assert.Equal(Exif, after.Exif);
            Assert.Equal(TestImages.Xmp, after.Xmp);
        }
    }

    [Fact]
    public async Task An_avif_converted_to_jpeg_keeps_its_exif_and_xmp()
    {
        using var dir = new TempDirectory();
        var avif = await AvifWithMetadataAsync(dir);
        await RequireWindowsDecoder(avif);

        var result = await new CompressionPipeline([.. Platform.ImageEngines()]).ProcessAsync(avif, KeepMetadata with
        {
            Mode = CompressionMode.Lossy, TargetFormat = MediaFormat.Jpeg,
        }, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        var after = MetadataTransfer.Read(result.OutputPath!, MediaFormat.Jpeg)!;
        Assert.Equal(Exif, after.Exif);
        Assert.Equal(TestImages.Xmp, after.Xmp);
    }
}
