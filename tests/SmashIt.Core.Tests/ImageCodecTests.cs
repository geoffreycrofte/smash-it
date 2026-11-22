using SmashIt.Core.Formats;
using SmashIt.Core.Imaging;
using SmashIt.Core.Metadata;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Tests;

/// <summary>The system's image codec (WIC on Windows, libvips elsewhere) keeps the same promises.</summary>
public class ImageCodecTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static IImageCodec Codec => Platform.ImageCodec;

    /// <summary>A 40×20 JPEG whose EXIF says "rotate 90° clockwise to show it" (shown as 20×40).</summary>
    private static Task<string> RotatedJpegAsync(TempDirectory dir) =>
        TestImages.WriteAsync(dir.File("rotated.jpg"), MediaFormat.Jpeg, 40, 20,
            new ImageMetadata { SourceFormat = MediaFormat.Jpeg, Exif = TestImages.Exif(6) });

    [Fact]
    public async Task Sizes_are_read_as_stored()
    {
        using var dir = new TempDirectory();
        var jpeg = await RotatedJpegAsync(dir);

        Assert.Equal((40u, 20u), await Codec.ReadSizeAsync(jpeg, Ct));
        Assert.Equal(1u, await Codec.ReadFrameCountAsync(jpeg, Ct));
    }

    [Fact]
    public async Task Unreadable_files_give_no_size_and_no_frames()
    {
        using var dir = new TempDirectory();
        var path = dir.File("broken.jpg");
        await File.WriteAllBytesAsync(path, [0xFF, 0xD8, 0xFF, 0xE0, 1, 2, 3], Ct);

        Assert.Null(await Codec.ReadSizeAsync(path, Ct));
        Assert.Equal(0u, await Codec.ReadFrameCountAsync(path, Ct));
    }

    [Theory]
    [InlineData(true, 20u, 40u)]
    [InlineData(false, 40u, 20u)]
    public async Task The_png_follows_the_orientation_when_asked(bool applyOrientation, uint width, uint height)
    {
        using var dir = new TempDirectory();
        var jpeg = await RotatedJpegAsync(dir);
        var png = dir.File("out.png");

        var frames = await Codec.WritePngAsync(jpeg, png, new PngConversion(applyOrientation, ConvertToSrgb: false), Ct);

        Assert.Equal(1u, frames);
        Assert.Equal(MediaFormat.Png, FormatDetector.DetectFile(png));
        Assert.Equal((width, height), await Codec.ReadSizeAsync(png, Ct));
    }

    [Fact]
    public async Task Scaling_applies_to_the_stored_image_then_it_is_rotated()
    {
        using var dir = new TempDirectory();
        var jpeg = await TestImages.WriteAsync(dir.File("big.jpg"), MediaFormat.Jpeg, 300, 102,
            new ImageMetadata { SourceFormat = MediaFormat.Jpeg, Exif = TestImages.Exif(6) });
        var png = dir.File("out.png");

        await Codec.WritePngAsync(jpeg, png, new PngConversion(ApplyOrientation: true, ConvertToSrgb: true, Scale: 0.5, Fast: true), Ct);

        // 300×102 halved: 150×51, then turned.
        Assert.Equal((51u, 150u), await Codec.ReadSizeAsync(png, Ct));
    }

    [Fact]
    public async Task An_animated_gif_counts_its_frames()
    {
        var gifsicle = ExternalTool.Find("gifsicle");
        if (gifsicle is null) Assert.Skip("gifsicle absent : lancez tools\\fetch-tools.ps1.");
        using var dir = new TempDirectory();
        var frames = new[]
        {
            await TestImages.WriteAsync(dir.File("f1.gif"), MediaFormat.Gif, 60, 40, noisy: false),
            await TestImages.WriteAsync(dir.File("f2.gif"), MediaFormat.Gif, 60, 40),
            await TestImages.WriteAsync(dir.File("f3.gif"), MediaFormat.Gif, 60, 40, noisy: false),
        };
        var gif = dir.File("anim.gif");
        await ExternalTool.RunAsync(gifsicle, ["--merge", .. frames, "--output", gif], Ct);

        Assert.Equal(3u, await Codec.ReadFrameCountAsync(gif, Ct));
        Assert.Equal(3u, await Codec.WritePngAsync(gif, dir.File("first.png"), new PngConversion(false, false), Ct));
    }

    [Fact]
    public async Task A_jpeg_is_resized_to_the_exact_size()
    {
        using var dir = new TempDirectory();
        var jpeg = await TestImages.WriteAsync(dir.File("page.jpg"), MediaFormat.Jpeg, 640, 480, noisy: false);
        var output = dir.File("small.jpg");

        await Codec.ResizeJpegAsync(jpeg, output, 213, 160, grayscale: false, 75, Ct);

        Assert.Equal(MediaFormat.Jpeg, FormatDetector.DetectFile(output));
        Assert.Equal((213u, 160u), await Codec.ReadSizeAsync(output, Ct));
        Assert.Equal(3, JpegComponents(await File.ReadAllBytesAsync(output, Ct)));
    }

    /// <summary>The number of color components in the JPEG's start-of-frame segment.</summary>
    private static int JpegComponents(byte[] jpeg)
    {
        for (var i = 2; i + 9 < jpeg.Length;)
        {
            if (jpeg[i] != 0xFF) { i++; continue; }
            var marker = jpeg[i + 1];
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC) return jpeg[i + 9];
            i += 2 + ((jpeg[i + 2] << 8) | jpeg[i + 3]);
        }
        throw new InvalidDataException("Pas de segment SOF.");
    }
}
