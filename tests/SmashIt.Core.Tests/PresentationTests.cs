using System.IO.Compression;
using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Formats;
using SmashIt.Core.Presentations;
using static SmashIt.Core.Tests.TestPresentation;
using CompressionMode = SmashIt.Core.Compression.CompressionMode;

namespace SmashIt.Core.Tests;

/// <summary>PowerPoint and Keynote files: the pictures inside are compressed, nothing else changes.</summary>
public class PresentationTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly CompressionOptions Lossy = new()
    {
        Mode = CompressionMode.Lossy, Quality = 75, PdfMaxImageDpi = 150, OriginalHandling = OriginalHandling.KeepAlongside,
    };

    [Theory]
    [InlineData(3000, 2000, 4.0, 150, 600)]   // 600 px are enough for 4 inches at 150 dpi
    [InlineData(3000, 2000, 8.0, 300, 0)]     // print: 2400 px needed, 3000 is only 1.25× finer: kept…
    [InlineData(3000, 2000, 8.0, 150, 1200)]  // …screen: 2.5× finer than needed, scaled
    [InlineData(3000, 2000, 14.0, 150, 0)]    // shown large, only 1.4× finer: kept
    [InlineData(3000, 2000, 4.0, 0, 0)]       // no resolution limit
    public void Pictures_much_finer_than_their_slide_size_get_a_target(uint width, uint height, double inches, int dpi, int expected)
    {
        var shown = new SlideImageSizes.Inches(inches, inches * height / width);
        var target = SlideImageSizes.TargetLongSide(width, height, shown, dpi);
        Assert.Equal(expected, target);
    }

    [Fact]
    public void Unknown_sizes_never_scale()
    {
        Assert.Equal(0, SlideImageSizes.TargetLongSide(3000, 2000, null, 150));
        // Only 1.2 × finer than needed: left alone, like in PDFs.
        Assert.Equal(0, SlideImageSizes.TargetLongSide(3000, 2000, new SlideImageSizes.Inches(16.7, 11.1), 150));
    }

    [Fact]
    public async Task Slide_sizes_are_read_from_plain_pictures_only()
    {
        using var dir = new TempDirectory();
        var jpeg = await TestImages.WriteAsync(dir.File("photo.jpg"), MediaFormat.Jpeg, 300, 200);
        var deck = Write(dir.File("deck.pptx"),
            new Picture(jpeg, 4 * Inch, 2 * Inch + Inch * 2 / 3),
            new Picture(jpeg, 2 * Inch, 1 * Inch, Grouped: true));

        using var zip = ZipFile.OpenRead(deck);
        var sizes = SlideImageSizes.Read(zip);

        var media = sizes.Keys.Order().ToList();
        Assert.Equal(2, media.Count);
        var shown = sizes[media[0]]!.Value;
        Assert.Equal(4, shown.Width, 3);
        Assert.Equal(2 + 2 / 3.0, shown.Height, 3);
        Assert.Null(sizes[media[1]]); // inside a group
    }

    [Fact]
    public async Task A_deck_gets_lighter_and_stays_valid()
    {
        using var dir = new TempDirectory();
        var photo = await TestImages.WriteAsync(dir.File("photo.jpg"), MediaFormat.Jpeg, 2400, 1600, noisy: false);
        var deck = Write(dir.File("deck.pptx"), new Picture(photo, 4 * Inch, 2 * Inch + Inch * 2 / 3));
        Assert.Empty(Validate(deck));

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(deck, Lossy, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.Equal("package", result.CompressorId);
        Assert.True(result.FinalSize < result.OriginalSize / 2, $"{result.FinalSize} / {result.OriginalSize}");
        Assert.Empty(Validate(result.OutputPath!));
        Assert.Equal(EntryNames(deck), EntryNames(result.OutputPath!));
        // 4 inches at 150 dpi.
        var image = await ExtractAsync(result.OutputPath!, "ppt/media/", dir.File("out.jpg"));
        Assert.Equal((600u, 400u), await TestImages.SizeAsync(image));
    }

    [Fact]
    public async Task Lossless_mode_keeps_every_picture_at_its_size()
    {
        using var dir = new TempDirectory();
        var photo = await TestImages.WriteAsync(dir.File("photo.png"), MediaFormat.Png, 1200, 800, noisy: false);
        var deck = Write(dir.File("deck.pptx"), new Picture(photo, 2 * Inch, 1 * Inch + Inch / 3));

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(deck, Lossy with { Mode = CompressionMode.Lossless }, Ct);

        Assert.True(result.Status is CompressionStatus.Optimized or CompressionStatus.AlreadyOptimal, result.Message);
        var image = await ExtractAsync(result.OutputPath ?? deck, "ppt/media/", dir.File("out.png"));
        Assert.Equal((1200u, 800u), await TestImages.SizeAsync(image));
        Assert.Empty(Validate(result.OutputPath ?? deck));
    }

    [Fact]
    public async Task A_keynote_file_keeps_its_picture_sizes_names_and_stored_entries()
    {
        using var dir = new TempDirectory();
        var photo = await TestImages.WriteAsync(dir.File("photo.jpg"), MediaFormat.Jpeg, 1600, 1000, noisy: false);
        var index = Enumerable.Range(0, 5000).Select(i => (byte)(i * 31)).ToArray();
        var key = dir.File("talk.key");
        using (var zip = ZipFile.Open(key, ZipArchiveMode.Create))
        {
            await using (var s = zip.CreateEntry("Index/Document.iwa", CompressionLevel.NoCompression).Open()) await s.WriteAsync(index, Ct);
            zip.CreateEntryFromFile(photo, "Data/photo-1234.jpg", CompressionLevel.NoCompression);
        }
        Assert.Equal(MediaFormat.Keynote, FormatDetector.DetectFile(key));

        var result = await SmashItEngine.CreateDefaultPipeline().ProcessAsync(key, Lossy with { Quality = 60 }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        using var output = ZipFile.OpenRead(result.OutputPath!);
        Assert.Equal(["Index/Document.iwa", "Data/photo-1234.jpg"], output.Entries.Select(e => e.FullName));
        Assert.All(output.Entries, e => Assert.Equal(e.Length, e.CompressedLength)); // still stored
        await using (var s = output.GetEntry("Index/Document.iwa")!.Open())
        {
            var copy = new MemoryStream();
            await s.CopyToAsync(copy, Ct);
            Assert.Equal(index, copy.ToArray());
        }
        var image = await ExtractAsync(result.OutputPath!, "Data/", dir.File("out.jpg"));
        Assert.Equal((1600u, 1000u), await TestImages.SizeAsync(image));
    }

    [Fact]
    public void A_key_file_that_is_not_a_zip_is_not_a_presentation()
    {
        using var dir = new TempDirectory();
        var pem = dir.File("server.key");
        File.WriteAllText(pem, "-----BEGIN PRIVATE KEY-----\nMIIE\n-----END PRIVATE KEY-----\n");
        Assert.Equal(MediaFormat.Unknown, FormatDetector.DetectFile(pem));
    }

    private static List<string> EntryNames(string path)
    {
        using var zip = ZipFile.OpenRead(path);
        return zip.Entries.Select(e => e.FullName).ToList();
    }

    private static async Task<string> ExtractAsync(string package, string folder, string to)
    {
        using var zip = ZipFile.OpenRead(package);
        var entry = zip.Entries.First(e => e.FullName.StartsWith(folder, StringComparison.Ordinal));
        await using var source = entry.Open();
        await using var target = File.Create(to);
        await source.CopyToAsync(target, Ct);
        return to;
    }
}
