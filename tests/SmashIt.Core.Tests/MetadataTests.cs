using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;
using SmashIt.Core.Metadata;

namespace SmashIt.Core.Tests;

public class MetadataTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ImageMetadata FullMetadata(MediaFormat format, int orientation = 1) => new()
    {
        SourceFormat = format,
        Exif = TestImages.Exif(orientation),
        Icc = TestImages.FakeIcc,
        Xmp = TestImages.Xmp,
    };

    [Fact]
    public void Orientation_can_be_read_patched_and_created()
    {
        var exif = TestImages.Exif(6);
        Assert.Equal(6, ExifOrientation.Read(exif));
        Assert.Equal(1, ExifOrientation.Read(ExifOrientation.WithOrientation(exif, 1)));
        Assert.Equal(6, ExifOrientation.Read(exif)); // original untouched
        Assert.Equal(8, ExifOrientation.Read(ExifOrientation.CreateMinimal(8)));
        Assert.Null(ExifOrientation.Read([1, 2, 3]));
    }

    [Theory]
    [InlineData(MediaFormat.Jpeg)]
    [InlineData(MediaFormat.Png)]
    public async Task Metadata_round_trips_and_the_image_stays_readable(MediaFormat format)
    {
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("a" + MediaFormatInfo.ExtensionOf(format)), format, 32, 16, FullMetadata(format));

        var read = MetadataTransfer.Read(path, format);

        Assert.NotNull(read);
        Assert.Equal(TestImages.Exif(1), read.Exif);
        Assert.Equal(TestImages.FakeIcc, read.Icc);
        Assert.Equal(TestImages.Xmp, read.Xmp);
        Assert.Equal((32u, 16u), await TestImages.SizeAsync(path));
    }

    [Fact]
    public void Large_icc_profiles_are_split_across_jpeg_segments()
    {
        var icc = Enumerable.Range(0, 150_000).Select(i => (byte)i).ToArray();
        byte[] jpeg = [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x02, 0xFF, 0xD9];

        var written = JpegMetadataIO.Write(jpeg, new ImageMetadata { SourceFormat = MediaFormat.Jpeg, Icc = icc });

        Assert.Equal(icc, JpegMetadataIO.Read(written).Icc);
    }

    [Fact]
    public void Stripping_keeps_the_color_profile_and_the_orientation_only()
    {
        var selected = MetadataTransfer.Select(
            FullMetadata(MediaFormat.Jpeg, orientation: 6), new CompressionOptions { StripMetadata = true },
            orientationBaked: false, MediaFormat.Jpeg);

        Assert.Equal(TestImages.FakeIcc, selected.Icc);
        Assert.Null(selected.Xmp);
        Assert.Equal(ExifOrientation.CreateMinimal(6), selected.Exif);
    }

    [Fact]
    public void Stripping_an_upright_image_removes_exif_entirely()
    {
        var selected = MetadataTransfer.Select(
            FullMetadata(MediaFormat.Jpeg), new CompressionOptions { StripMetadata = true },
            orientationBaked: false, MediaFormat.Jpeg);

        Assert.Null(selected.Exif);
    }

    [Fact]
    public void Baked_orientation_is_reset_in_kept_exif()
    {
        var selected = MetadataTransfer.Select(
            FullMetadata(MediaFormat.Jpeg, orientation: 6), new CompressionOptions { StripMetadata = false },
            orientationBaked: true, MediaFormat.Png);

        Assert.Equal(1, ExifOrientation.Read(selected.Exif));
        Assert.Equal(TestImages.Xmp, selected.Xmp);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Lossy_jpeg_compression_honors_the_strip_option(bool strip)
    {
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("a.jpg"), MediaFormat.Jpeg, 128, 64, FullMetadata(MediaFormat.Jpeg));
        var pipeline = new CompressionPipeline([new WicImageCompressor()]);

        var result = await pipeline.ProcessAsync(path, new CompressionOptions
        {
            Mode = CompressionMode.Lossy,
            Quality = 40,
            StripMetadata = strip,
            OriginalHandling = OriginalHandling.Overwrite,
        }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        var after = MetadataTransfer.Read(path, MediaFormat.Jpeg)!;
        Assert.Equal(TestImages.FakeIcc, after.Icc);
        if (strip)
        {
            Assert.Null(after.Exif);
            Assert.Null(after.Xmp);
        }
        else
        {
            Assert.Equal(TestImages.Exif(1), after.Exif);
            Assert.Equal(TestImages.Xmp, after.Xmp);
        }
    }

    [Fact]
    public async Task Same_format_keeps_pixels_unrotated_and_the_orientation_tag()
    {
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("a.jpg"), MediaFormat.Jpeg, 128, 64, FullMetadata(MediaFormat.Jpeg, 6));
        var pipeline = new CompressionPipeline([new WicImageCompressor()]);

        await pipeline.ProcessAsync(path, new CompressionOptions
        {
            Mode = CompressionMode.Lossy, Quality = 40, OriginalHandling = OriginalHandling.Overwrite,
        }, Ct);

        Assert.Equal((128u, 64u), await TestImages.SizeAsync(path));
        Assert.Equal(6, ExifOrientation.Read(MetadataTransfer.Read(path, MediaFormat.Jpeg)!.Exif));
    }

    [Fact]
    public async Task Conversion_rotates_the_pixels_and_resets_the_orientation()
    {
        using var dir = new TempDirectory();
        var path = await TestImages.WriteAsync(dir.File("a.jpg"), MediaFormat.Jpeg, 128, 64, FullMetadata(MediaFormat.Jpeg, 6));
        var pipeline = new CompressionPipeline([new WicImageCompressor()]);

        var result = await pipeline.ProcessAsync(path, new CompressionOptions
        {
            TargetFormat = MediaFormat.Png, StripMetadata = false, OriginalHandling = OriginalHandling.KeepAlongside,
        }, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        Assert.Equal(dir.File("a-compressed.png"), result.OutputPath);
        Assert.Equal((64u, 128u), await TestImages.SizeAsync(result.OutputPath!));
        var after = MetadataTransfer.Read(result.OutputPath!, MediaFormat.Png)!;
        Assert.Equal(1, ExifOrientation.Read(after.Exif));
        Assert.Equal(TestImages.FakeIcc, after.Icc);
    }

    [Fact]
    public async Task Stripping_alone_can_shrink_a_png()
    {
        using var dir = new TempDirectory();
        var bigXmp = new ImageMetadata
        {
            SourceFormat = MediaFormat.Png,
            Xmp = Enumerable.Repeat((byte)'x', 20_000).ToArray(),
        };
        var path = await TestImages.WriteAsync(dir.File("a.png"), MediaFormat.Png, 32, 32, bigXmp);
        var pipeline = new CompressionPipeline([new WicImageCompressor()]);

        var result = await pipeline.ProcessAsync(path, new CompressionOptions { OriginalHandling = OriginalHandling.Overwrite }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.Null(MetadataTransfer.Read(path, MediaFormat.Png)!.Xmp);
    }
}
