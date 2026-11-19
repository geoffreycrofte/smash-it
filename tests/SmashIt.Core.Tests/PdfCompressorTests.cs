using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Pdf;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Tests;

/// <summary>Runs qpdf for real. Skipped when tools\fetch-tools.ps1 has not been run.</summary>
public class PdfCompressorTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static Qpdf RequireQpdf() =>
        Qpdf.Find() ?? throw Xunit.Sdk.SkipException.ForSkip("qpdf absent : lancez tools\\fetch-tools.ps1.");

    private static CompressionPipeline Pipeline() => new([new PdfCompressor()]);

    [Fact]
    public async Task Test_pdf_is_valid()
    {
        var qpdf = RequireQpdf();
        using var dir = new TempDirectory();
        var pdf = await TestPdf.WriteAsync(dir.File("doc.pdf"));

        await qpdf.CheckAsync(pdf, Ct);
        Assert.Equal(1, await qpdf.CountPagesAsync(pdf, Ct));
    }

    [Fact]
    public async Task Lossless_mode_shrinks_the_pdf_and_keeps_its_structure()
    {
        var qpdf = RequireQpdf();
        using var dir = new TempDirectory();
        var pdf = await TestPdf.WriteAsync(dir.File("doc.pdf"));
        var before = await ExportAsync(qpdf, pdf, dir.File("before"));
        var originalSize = new FileInfo(pdf).Length;

        var result = await Pipeline().ProcessAsync(pdf, new CompressionOptions { OriginalHandling = OriginalHandling.Overwrite }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.True(new FileInfo(pdf).Length < originalSize);
        await qpdf.CheckAsync(pdf, Ct);

        var after = await ExportAsync(qpdf, pdf, dir.File("after"));
        var jpeg = Assert.Single(after.Images(), i => i.IsPlainJpeg);
        Assert.Equal(400, jpeg.Dictionary["/Width"]!.GetValue<int>());
        Assert.Equal(2, after.Images().Count());
        // Lossless: the Flate image is still Flate.
        Assert.Contains(after.Images(), i => i.Dictionary["/Filter"]!.ToJsonString().Contains("FlateDecode"));
        Assert.Equal(before.Images().Count(), after.Images().Count());
    }

    [Fact]
    public async Task Lossy_mode_recompresses_images_as_jpeg()
    {
        var qpdf = RequireQpdf();
        using var dir = new TempDirectory();
        var lossless = await TestPdf.WriteAsync(dir.File("lossless.pdf"));
        var lossy = await TestPdf.WriteAsync(dir.File("lossy.pdf"));

        await Pipeline().ProcessAsync(lossless, new CompressionOptions { OriginalHandling = OriginalHandling.Overwrite }, Ct);
        var result = await Pipeline().ProcessAsync(lossy, new CompressionOptions
        {
            Mode = CompressionMode.Lossy, Quality = 50, OriginalHandling = OriginalHandling.Overwrite,
        }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.True(new FileInfo(lossy).Length < new FileInfo(lossless).Length);
        await qpdf.CheckAsync(lossy, Ct);
        var after = await ExportAsync(qpdf, lossy, dir.File("after"));
        Assert.All(after.Images(), i => Assert.True(i.IsPlainJpeg));
    }

    [Fact]
    public async Task Keeping_originals_writes_name_compressed_pdf()
    {
        RequireQpdf();
        using var dir = new TempDirectory();
        var pdf = await TestPdf.WriteAsync(dir.File("report.pdf"));

        var result = await Pipeline().ProcessAsync(pdf, new CompressionOptions { OriginalHandling = OriginalHandling.KeepAlongside }, Ct);

        Assert.Equal(dir.File("report-compressed.pdf"), result.OutputPath);
        Assert.True(File.Exists(pdf));
    }

    [Fact]
    public async Task Signed_pdfs_are_left_untouched()
    {
        RequireQpdf();
        using var dir = new TempDirectory();
        var pdf = await TestPdf.WriteAsync(dir.File("signed.pdf"), signed: true);
        var before = await File.ReadAllBytesAsync(pdf, Ct);

        var result = await Pipeline().ProcessAsync(pdf, new CompressionOptions { OriginalHandling = OriginalHandling.Overwrite }, Ct);

        Assert.Equal(CompressionStatus.Unsupported, result.Status);
        Assert.Contains("Signed PDF", result.Message);
        Assert.Equal(before, await File.ReadAllBytesAsync(pdf, Ct));
    }

    [Fact]
    public async Task Encrypted_pdfs_are_left_untouched()
    {
        var qpdf = RequireQpdf();
        using var dir = new TempDirectory();
        var plain = await TestPdf.WriteAsync(dir.File("plain.pdf"));
        var encrypted = dir.File("encrypted.pdf");
        await ExternalTool.RunRawAsync(qpdf.Executable,
            [plain, "--encrypt", "--user-password=", "--owner-password=owner", "--bits=256", "--", encrypted], Ct);
        Assert.True(await qpdf.IsEncryptedAsync(encrypted, Ct));

        var result = await Pipeline().ProcessAsync(encrypted, new CompressionOptions { OriginalHandling = OriginalHandling.Overwrite }, Ct);

        Assert.Equal(CompressionStatus.Unsupported, result.Status);
        Assert.Contains("Encrypted PDF", result.Message);
    }

    private static async Task<QpdfJson> ExportAsync(Qpdf qpdf, string pdf, string directory)
    {
        Directory.CreateDirectory(directory);
        var json = Path.Combine(directory, "doc.json");
        await qpdf.RunAsync([pdf, "--json-output", "--json-key=qpdf", "--json-stream-data=file",
            $"--json-stream-prefix={Path.Combine(directory, "s")}", json], Ct);
        return QpdfJson.Load(json);
    }
}
