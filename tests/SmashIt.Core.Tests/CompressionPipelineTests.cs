using SmashIt.Core.Compression;
using SmashIt.Core.Formats;

namespace SmashIt.Core.Tests;

public class CompressionPipelineTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly CompressionOptions Overwrite = new() { OriginalHandling = OriginalHandling.Overwrite };

    [Fact]
    public async Task Replaces_the_original_when_the_result_is_smaller()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 1000);
        var pipeline = new CompressionPipeline([new FakeCompressor("small", 400)]);

        var result = await pipeline.ProcessAsync(file, Overwrite, TestContext.Current.CancellationToken);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.Equal(1000, result.OriginalSize);
        Assert.Equal(400, result.FinalSize);
        Assert.Equal(600, result.SavedBytes);
        Assert.Equal(400, new FileInfo(file).Length);
        Assert.Single(Directory.GetFiles(dir.Path));
    }

    [Fact]
    public async Task Keeps_the_smallest_result_among_compressors()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 1000);
        var pipeline = new CompressionPipeline(
            [new FakeCompressor("medium", 700), new FakeCompressor("best", 300), new FakeCompressor("bad", 900)]);

        var result = await pipeline.ProcessAsync(file, Overwrite, TestContext.Current.CancellationToken);

        Assert.Equal("best", result.CompressorId);
        Assert.Equal(300, new FileInfo(file).Length);
    }

    [Fact]
    public async Task Leaves_the_original_untouched_when_nothing_is_smaller()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 1000);
        var before = await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken);
        var pipeline = new CompressionPipeline([new FakeCompressor("bigger", 1200)]);

        var result = await pipeline.ProcessAsync(file, Overwrite, TestContext.Current.CancellationToken);

        Assert.Equal(CompressionStatus.AlreadyOptimal, result.Status);
        Assert.Equal(before, await File.ReadAllBytesAsync(file, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Saves_next_to_the_original_when_asked_to_keep_it()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 1000);
        var pipeline = new CompressionPipeline([new FakeCompressor("small", 400)]);

        var result = await pipeline.ProcessAsync(
            file, Overwrite with { OriginalHandling = OriginalHandling.KeepAlongside },
            TestContext.Current.CancellationToken);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.Equal(dir.File("a.smashed.png"), result.OutputPath);
        Assert.Equal(1000, new FileInfo(file).Length);
        Assert.Equal(400, new FileInfo(result.OutputPath!).Length);
    }

    [Fact]
    public async Task Conversion_writes_a_new_file_and_keeps_the_original()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 1000);
        var pipeline = new CompressionPipeline([new FakeCompressor("conv", 1500, MediaFormat.Jpeg)]);

        var result = await pipeline.ProcessAsync(
            file, Overwrite with { TargetFormat = MediaFormat.Jpeg }, TestContext.Current.CancellationToken);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        Assert.Equal(dir.File("a.jpg"), result.OutputPath);
        Assert.True(File.Exists(file));
        Assert.Equal(1500, new FileInfo(result.OutputPath!).Length);
    }

    [Fact]
    public async Task Reports_unsupported_when_no_compressor_matches()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 100);
        var pipeline = new CompressionPipeline([new FakeCompressor("jpeg-only", 10, MediaFormat.Jpeg)]);

        var result = await pipeline.ProcessAsync(file, Overwrite, TestContext.Current.CancellationToken);

        Assert.Equal(CompressionStatus.Unsupported, result.Status);
    }

    [Fact]
    public async Task Reports_failure_with_the_compressor_message()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 100);
        var pipeline = new CompressionPipeline([new FakeCompressor("boom", -1)]);

        var result = await pipeline.ProcessAsync(file, Overwrite, TestContext.Current.CancellationToken);

        Assert.Equal(CompressionStatus.Failed, result.Status);
        Assert.Contains("boom failed", result.Message);
        Assert.Equal(100, new FileInfo(file).Length);
    }

    private static string WritePng(string path, int size)
    {
        var bytes = new byte[size];
        PngSignature.CopyTo(bytes, 0);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    /// <summary>Writes a file of a fixed size; a negative size makes it throw.</summary>
    private sealed class FakeCompressor(string id, int outputSize, MediaFormat output = MediaFormat.Png) : ICompressor
    {
        public string Id => id;
        public string DisplayName => id;

        public bool CanHandle(MediaFormat inputFormat, MediaFormat outputFormat, CompressionOptions options) =>
            outputFormat == output;

        public Task CompressAsync(string inputPath, string outputPath, MediaFormat outputFormat,
            CompressionOptions options, CancellationToken cancellationToken)
        {
            if (outputSize < 0) throw new InvalidOperationException($"{id} failed");
            File.WriteAllBytes(outputPath, new byte[outputSize]);
            return Task.CompletedTask;
        }
    }
}
