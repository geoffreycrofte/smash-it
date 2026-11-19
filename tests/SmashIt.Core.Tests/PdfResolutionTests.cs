using System.Text;
using System.Text.Json.Nodes;
using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Pdf;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Tests;

/// <summary>Scaling down images that are much finer than their size on the page.</summary>
public class PdfResolutionTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static List<(string Name, (double, double) Size)> Draws(string content)
    {
        var draws = new List<(string, (double, double))>();
        PdfContent.Walk(Encoding.Latin1.GetBytes(content), PdfMatrix.Identity, (name, ctm) => draws.Add((name, ctm.UnitSquareSize)));
        return draws;
    }

    [Fact]
    public void Follows_the_matrix_through_save_restore_and_nested_cm()
    {
        var draws = Draws("q 2 0 0 2 0 0 cm q 100 0 0 50 10 10 cm /A Do Q /B Do Q 30 0 0 20 0 0 cm /C Do");

        Assert.Equal([("A", (200d, 100d)), ("B", (2d, 2d)), ("C", (30d, 20d))], draws);
    }

    [Fact]
    public void Measures_rotated_images()
    {
        // 90° rotation: a = 0, b = 100, c = -50, d = 0.
        var (_, (width, height)) = Assert.Single(Draws("q 0 100 -50 0 200 0 cm /R Do Q"));

        Assert.Equal(100, width, 6);
        Assert.Equal(50, height, 6);
    }

    [Fact]
    public void Skips_strings_comments_and_inline_images()
    {
        var draws = Draws(
            "BT (a ) cm \\) Do ( nested ) ) Tj ET % 9 0 0 9 0 0 cm\n" +
            "<< /Tag /Skip >> BDC EMC [ (x) 2 ] TJ <48656C6C6F> Tj\n" +
            "BI /W 2 /H 2 /BPC 8 /CS /G ID \x00\xFFcm Do EI EI\n" +
            "q 10 0 0 10 0 0 cm /Im Do Q");

        Assert.Equal([("Im", (10d, 10d))], draws);
    }

    [Theory]
    // 2400 px over 300 pt (4.17 in) = 576 dpi → 150 dpi: 625×469.
    [InlineData(2400, 1800, 300, 225, 150, 625, 469)]
    // 400 px over 300 pt = 96 dpi: below 150, nothing to do.
    [InlineData(400, 300, 300, 225, 150, null, null)]
    // 200 dpi is less than 1.5 × 150: not worth it.
    [InlineData(833, 625, 300, 225, 150, null, null)]
    // Stretched image: the less dense direction decides (here 300 dpi wide, 600 dpi tall → scale 0.5).
    [InlineData(1250, 1875, 300, 225, 150, 625, 938)]
    public void Computes_the_new_size(int w, int h, double shownW, double shownH, int dpi, int? newW, int? newH)
    {
        var size = PdfCompressor.ResampledSize(w, h, shownW, shownH, dpi);

        Assert.Equal(newW, size?.Width);
        Assert.Equal(newH, size?.Height);
    }

    [Fact]
    public void Only_plain_gray_or_rgb_jpegs_are_resampled()
    {
        JsonObject Dict(string colorSpace, string extra = "") =>
            JsonNode.Parse($$"""{"/Subtype":"/Image","/Filter":"/DCTDecode","/Width":10,"/Height":10,"/BitsPerComponent":8,"/ColorSpace":{{colorSpace}}{{extra}}}""")!.AsObject();
        JsonNode? Resolve(JsonNode? node) => node;

        Assert.True(new PdfImageStream("obj:1 0 R", Dict("\"/DeviceRGB\""), "x").CanResample(Resolve, out var rgbGray));
        Assert.False(rgbGray);
        Assert.True(new PdfImageStream("obj:1 0 R", Dict("\"/DeviceGray\""), "x").CanResample(Resolve, out var gray));
        Assert.True(gray);
        Assert.True(new PdfImageStream("obj:1 0 R", Dict("[\"/ICCBased\",{\"/N\":3}]"), "x").CanResample(Resolve, out _));
        Assert.False(new PdfImageStream("obj:1 0 R", Dict("\"/DeviceCMYK\""), "x").CanResample(Resolve, out _));
        Assert.False(new PdfImageStream("obj:1 0 R", Dict("\"/DeviceRGB\"", ",\"/Decode\":[1,0,1,0,1,0]"), "x").CanResample(Resolve, out _));
    }

    private static Qpdf RequireQpdf() =>
        Qpdf.Find() ?? throw Xunit.Sdk.SkipException.ForSkip("qpdf absent : lancez tools\\fetch-tools.ps1.");

    private static async Task<QpdfJson> ExportAsync(Qpdf qpdf, string pdf, string directory)
    {
        Directory.CreateDirectory(directory);
        var json = Path.Combine(directory, "doc.json");
        await qpdf.RunAsync([pdf, "--json-output", "--json-key=qpdf", "--json-stream-data=file",
            $"--json-stream-prefix={Path.Combine(directory, "s")}", json], Ct);
        return QpdfJson.Load(json);
    }

    private static readonly CompressionOptions Lossy150 = new()
    {
        Mode = CompressionMode.Lossy, Quality = 80, PdfMaxImageDpi = 150, OriginalHandling = OriginalHandling.Overwrite,
    };

    [Theory]
    [InlineData(false)]
    [InlineData(true)] // drawn through a form XObject scaled by half
    public async Task Scales_down_an_image_far_finer_than_its_size_on_the_page(bool imageInForm)
    {
        var qpdf = RequireQpdf();
        using var dir = new TempDirectory();
        var pdf = await TestPdf.WriteAsync(dir.File("scan.pdf"), jpegWidth: 2400, jpegHeight: 1800, imageInForm: imageInForm);
        var before = await ExportAsync(qpdf, pdf, dir.File("before"));
        Assert.Equal((300d, 225d), before.ImageDisplaySizes()["obj:4 0 R"]);
        var originalSize = new FileInfo(pdf).Length;

        var result = await new CompressionPipeline([new PdfCompressor()]).ProcessAsync(pdf, Lossy150, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        await qpdf.CheckAsync(pdf, Ct);
        var after = await ExportAsync(qpdf, pdf, dir.File("after"));
        var image = Assert.Single(after.Images(), i => i.Width == 625);
        Assert.Equal(469, image.Height);
        Assert.True(new FileInfo(pdf).Length < originalSize / 4, $"{originalSize} → {new FileInfo(pdf).Length}");
    }

    [Fact]
    public async Task Keeps_the_resolution_in_lossless_mode_or_when_turned_off()
    {
        var qpdf = RequireQpdf();
        using var dir = new TempDirectory();
        var lossless = await TestPdf.WriteAsync(dir.File("lossless.pdf"), jpegWidth: 2400, jpegHeight: 1800);
        var off = await TestPdf.WriteAsync(dir.File("off.pdf"), jpegWidth: 2400, jpegHeight: 1800);
        var pipeline = new CompressionPipeline([new PdfCompressor()]);

        await pipeline.ProcessAsync(lossless, Lossy150 with { Mode = CompressionMode.Lossless }, Ct);
        await pipeline.ProcessAsync(off, Lossy150 with { PdfMaxImageDpi = 0 }, Ct);

        Assert.Contains((await ExportAsync(qpdf, lossless, dir.File("a"))).Images(), i => i.Width == 2400);
        Assert.Contains((await ExportAsync(qpdf, off, dir.File("b"))).Images(), i => i.Width == 2400);
    }
}
