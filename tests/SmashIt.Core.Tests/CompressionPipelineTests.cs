using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.IO;

namespace SmashIt.Core.Tests;

public class CompressionPipelineTests
{
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    private static readonly CompressionOptions Overwrite = new() { OriginalHandling = OriginalHandling.Overwrite };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Replaces_the_original_when_the_result_is_smaller()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 1000);
        var pipeline = new CompressionPipeline([new FakeCompressor("small", 400)]);

        var result = await pipeline.ProcessAsync(file, Overwrite, Ct);

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

        var result = await pipeline.ProcessAsync(file, Overwrite, Ct);

        Assert.Equal("best", result.CompressorId);
        Assert.Equal(300, new FileInfo(file).Length);
    }

    [Fact]
    public async Task Runs_optimizers_on_the_output_of_other_engines()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 1000);
        var optimizer = new FakeCompressor("opt", 200, isOptimizer: true) { OnlyLosslessSameFormat = true };
        var pipeline = new CompressionPipeline([new FakeCompressor("encoder", 600, MediaFormat.Jpeg), optimizer]);

        var result = await pipeline.ProcessAsync(file, Overwrite with { TargetFormat = MediaFormat.Jpeg }, Ct);

        Assert.Equal("encoder+opt", result.CompressorId);
        Assert.Equal(200, result.FinalSize);
        Assert.Equal(MediaFormat.Jpeg, optimizer.LastInputFormat);
    }

    [Fact]
    public async Task Skips_unavailable_compressors()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 1000);
        var pipeline = new CompressionPipeline(
            [new FakeCompressor("missing", 10) { Available = false }, new FakeCompressor("ok", 500)]);

        var result = await pipeline.ProcessAsync(file, Overwrite, Ct);

        Assert.Equal("ok", result.CompressorId);
    }

    [Fact]
    public async Task Leaves_the_original_untouched_when_nothing_is_smaller()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 1000);
        var before = await File.ReadAllBytesAsync(file, Ct);
        var pipeline = new CompressionPipeline([new FakeCompressor("bigger", 1200)]);

        var result = await pipeline.ProcessAsync(file, Overwrite, Ct);

        Assert.Equal(CompressionStatus.AlreadyOptimal, result.Status);
        Assert.Equal(before, await File.ReadAllBytesAsync(file, Ct));
    }

    [Fact]
    public async Task Keeping_the_original_saves_the_result_as_name_compressed()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("photo.png"), 1000);
        var pipeline = new CompressionPipeline([new FakeCompressor("small", 400)]);

        var result = await pipeline.ProcessAsync(
            file, Overwrite with { OriginalHandling = OriginalHandling.KeepAlongside }, Ct);

        Assert.Equal(CompressionStatus.Optimized, result.Status);
        Assert.Equal(dir.File("photo-compressed.png"), result.OutputPath);
        Assert.Equal(1000, new FileInfo(file).Length);
        Assert.Equal(400, new FileInfo(result.OutputPath!).Length);
    }

    [Fact]
    public async Task Keeping_the_original_never_overwrites_an_existing_compressed_file()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("photo.png"), 1000);
        File.WriteAllText(dir.File("photo-compressed.png"), "already here");
        var pipeline = new CompressionPipeline([new FakeCompressor("small", 400)]);

        var result = await pipeline.ProcessAsync(
            file, Overwrite with { OriginalHandling = OriginalHandling.KeepAlongside }, Ct);

        Assert.Equal(dir.File("photo-compressed (2).png"), result.OutputPath);
        Assert.Equal("already here", File.ReadAllText(dir.File("photo-compressed.png")));
    }

    [Fact]
    public async Task Keeping_the_original_in_a_compressed_folder_keeps_the_original_name()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("photo.png"), 1000);
        var pipeline = new CompressionPipeline([new FakeCompressor("small", 400)]);
        var options = Overwrite with
        {
            OriginalHandling = OriginalHandling.KeepAlongside, KeptOriginalFolder = CompressionOptions.CompressedFolderName,
        };

        var first = await pipeline.ProcessAsync(file, options, Ct);
        var second = await pipeline.ProcessAsync(file, options, Ct);

        Assert.Equal(dir.File(@"compressed\photo.png"), first.OutputPath);
        Assert.Equal(400, new FileInfo(first.OutputPath!).Length);
        Assert.Equal(1000, new FileInfo(file).Length);
        // A second run never overwrites the first result.
        Assert.Equal(dir.File(@"compressed\photo (2).png"), second.OutputPath);
    }

    [Fact]
    public async Task An_already_optimal_file_is_copied_into_the_compressed_folder_only_when_asked()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("photo.png"), 1000);
        var pipeline = new CompressionPipeline([new FakeCompressor("bigger", 1200)]);
        var options = Overwrite with
        {
            OriginalHandling = OriginalHandling.KeepAlongside, KeptOriginalFolder = CompressionOptions.CompressedFolderName,
        };

        var skipped = await pipeline.ProcessAsync(file, options, Ct);
        Assert.Equal(CompressionStatus.AlreadyOptimal, skipped.Status);
        Assert.Null(skipped.OutputPath);
        Assert.False(Directory.Exists(dir.File("compressed")));

        var copied = await pipeline.ProcessAsync(file, options with { CopyAlreadyOptimal = true }, Ct);
        Assert.Equal(CompressionStatus.AlreadyOptimal, copied.Status);
        Assert.Equal(dir.File(@"compressed\photo.png"), copied.OutputPath);
        Assert.Equal(await File.ReadAllBytesAsync(file, Ct), await File.ReadAllBytesAsync(copied.OutputPath!, Ct));
        Assert.Equal(0, copied.SavedBytes);
    }

    [Fact]
    public void A_folder_drop_skips_the_results_of_an_earlier_run()
    {
        using var dir = new TempDirectory();
        Directory.CreateDirectory(dir.File(@"trip\compressed"));
        Directory.CreateDirectory(dir.File(@"trip\day 2\compressed"));
        File.WriteAllText(dir.File(@"trip\a.png"), "");
        File.WriteAllText(dir.File(@"trip\compressed\a.png"), "");
        File.WriteAllText(dir.File(@"trip\day 2\b.jpg"), "");
        File.WriteAllText(dir.File(@"trip\day 2\compressed\b.jpg"), "");

        var files = InputCollector.Expand([dir.File("trip")], CompressionOptions.CompressedFolderName).ToList();

        Assert.Equal([dir.File(@"trip\a.png"), dir.File(@"trip\day 2\b.jpg")], files.Order());
        // A "compressed" folder given on purpose is still compressed.
        Assert.Single(InputCollector.Expand([dir.File(@"trip\compressed")], CompressionOptions.CompressedFolderName));
    }

    [Fact]
    public async Task Conversion_keeping_the_original_writes_name_compressed_with_the_new_extension()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("photo.png"), 1000);
        var pipeline = new CompressionPipeline([new FakeCompressor("conv", 1500, MediaFormat.Jpeg)]);

        var result = await pipeline.ProcessAsync(
            file, new CompressionOptions { TargetFormat = MediaFormat.Jpeg, OriginalHandling = OriginalHandling.KeepAlongside }, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        Assert.Equal(dir.File("photo-compressed.jpg"), result.OutputPath);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public async Task Conversion_without_keeping_the_original_removes_it()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("photo.png"), 1000);
        var pipeline = new CompressionPipeline([new FakeCompressor("conv", 1500, MediaFormat.Jpeg)]);

        var result = await pipeline.ProcessAsync(file, Overwrite with { TargetFormat = MediaFormat.Jpeg }, Ct);

        Assert.Equal(CompressionStatus.Converted, result.Status);
        Assert.Equal(dir.File("photo.jpg"), result.OutputPath);
        Assert.False(File.Exists(file));
        Assert.Equal(1500, new FileInfo(result.OutputPath!).Length);
    }

    [Fact]
    public async Task A_conversion_not_asked_for_is_dropped_when_larger()
    {
        using var dir = new TempDirectory();
        var mkv = dir.File("clip.mkv");
        File.WriteAllBytes(mkv, [0x1A, 0x45, 0xDF, 0xA3, .. new byte[996]]);
        var pipeline = new CompressionPipeline([new FakeCompressor("video", 1500, MediaFormat.Mp4)]);

        var result = await pipeline.ProcessAsync(mkv, Overwrite, Ct);

        Assert.Equal(CompressionStatus.AlreadyOptimal, result.Status);
        Assert.True(File.Exists(mkv));
        Assert.False(File.Exists(dir.File("clip.mp4")));
    }

    [Fact]
    public async Task Reports_unsupported_when_no_compressor_matches()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 100);
        var pipeline = new CompressionPipeline([new FakeCompressor("jpeg-only", 10, MediaFormat.Jpeg)]);

        var result = await pipeline.ProcessAsync(file, Overwrite, Ct);

        Assert.Equal(CompressionStatus.Unsupported, result.Status);
    }

    [Fact]
    public async Task Reports_failure_with_the_compressor_message()
    {
        using var dir = new TempDirectory();
        var file = WritePng(dir.File("a.png"), 100);
        var pipeline = new CompressionPipeline([new FakeCompressor("boom", -1)]);

        var result = await pipeline.ProcessAsync(file, Overwrite, Ct);

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
    private sealed class FakeCompressor(string id, int outputSize, MediaFormat output = MediaFormat.Png, bool isOptimizer = false)
        : ICompressor
    {
        public string Id => id;
        public string DisplayName => id;
        public bool IsAvailable => Available;
        public bool IsOptimizer => isOptimizer;
        public bool Available { get; init; } = true;
        public bool OnlyLosslessSameFormat { get; init; }
        public MediaFormat? LastInputFormat { get; private set; }

        public bool CanHandle(MediaFormat inputFormat, MediaFormat outputFormat, CompressionOptions options) =>
            OnlyLosslessSameFormat
                ? inputFormat == outputFormat && options.Mode == CompressionMode.Lossless
                : outputFormat == output;

        public Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
        {
            if (outputSize < 0) throw new InvalidOperationException($"{id} failed");
            LastInputFormat = job.InputFormat;
            File.WriteAllBytes(job.OutputPath, new byte[outputSize]);
            return Task.CompletedTask;
        }
    }
}
