using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Imaging;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Compressors;

/// <summary>A compressor backed by a bundled command-line tool.</summary>
public abstract class ToolCompressor(string executable) : ICompressor
{
    protected string? ToolPath { get; } = ExternalTool.Find(executable);

    public abstract string Id { get; }

    public abstract string DisplayName { get; }

    public bool IsAvailable => ToolPath is not null;

    public virtual bool IsOptimizer => false;

    public abstract bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options);

    public abstract Task CompressAsync(CompressionJob job, CancellationToken cancellationToken);

    protected Task RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken) =>
        ExternalTool.RunAsync(
            ToolPath ?? throw new ExternalToolException(L10n.T($"{DisplayName} not found.", $"{DisplayName} est introuvable.")),
            arguments,
            cancellationToken);
}

/// <summary>oxipng: lossless PNG optimizer (MIT).</summary>
public sealed class OxipngCompressor() : ToolCompressor("oxipng.exe")
{
    public override string Id => "oxipng";

    public override string DisplayName => "oxipng";

    public override bool IsOptimizer => true;

    public override bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        input == MediaFormat.Png && output == MediaFormat.Png;

    public override Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var level = job.Options.Effort switch
        {
            CompressionEffort.Fast => "1",
            CompressionEffort.Maximum => "max",
            _ => "3",
        };

        // "safe" keeps the color chunks (iCCP, sRGB…); EXIF and text are handled by the pipeline.
        return RunAsync(
            ["--opt", level, "--strip", "safe", "--quiet", "--out", job.OutputPath, job.InputPath],
            cancellationToken);
    }
}

/// <summary>
/// jpegtran (libjpeg-turbo): lossless JPEG optimization — optimal Huffman tables and progressive encoding.
/// Both baseline and progressive are tried; progressive usually wins except on tiny images.
/// </summary>
public sealed class JpegtranCompressor() : ToolCompressor("jpegtran.exe")
{
    public override string Id => "jpegtran";

    public override string DisplayName => "jpegtran";

    public override bool IsOptimizer => true;

    public override bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        input == MediaFormat.Jpeg && output == MediaFormat.Jpeg;

    public override async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        // Metadata is rewritten by the pipeline afterwards.
        await RunAsync(
            ["-copy", "none", "-optimize", "-progressive", "-outfile", job.OutputPath, job.InputPath],
            cancellationToken);

        if (job.Options.Effort == CompressionEffort.Fast) return;

        var baseline = job.OutputPath + ".baseline.jpg";
        try
        {
            await RunAsync(["-copy", "none", "-optimize", "-outfile", baseline, job.InputPath], cancellationToken);
            if (new FileInfo(baseline).Length < new FileInfo(job.OutputPath).Length)
                File.Move(baseline, job.OutputPath, overwrite: true);
        }
        finally
        {
            File.Delete(baseline);
        }
    }
}

/// <summary>cwebp (libwebp): WebP encoder, lossy or lossless (BSD).</summary>
public sealed class CwebpCompressor() : ToolCompressor("cwebp.exe")
{
    public override string Id => "cwebp";

    public override string DisplayName => "cwebp";

    public override bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        output == MediaFormat.WebP && WicImageCompressor.CanRead(input);

    public override async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var source = job.OutputPath + ".src.png";
        try
        {
            await IntermediatePng.CreateAsync(job, source, cancellationToken);

            var options = job.Options;
            var args = new List<string>();
            if (options.Mode == CompressionMode.Lossless)
            {
                args.AddRange(["-lossless", "-z", options.Effort switch
                {
                    CompressionEffort.Fast => "3",
                    CompressionEffort.Maximum => "9",
                    _ => "6",
                }]);
            }
            else
            {
                args.AddRange(["-q", Math.Clamp(options.Quality, 0, 100).ToString(), "-m", options.Effort switch
                {
                    CompressionEffort.Fast => "2",
                    CompressionEffort.Maximum => "6",
                    _ => "4",
                }]);
            }

            args.AddRange(["-metadata", options.StripMetadata ? "icc" : "all", "-mt", "-quiet", source, "-o", job.OutputPath]);
            await RunAsync(args, cancellationToken);
        }
        finally
        {
            File.Delete(source);
        }
    }
}

/// <summary>avifenc (libavif): AVIF encoder, lossy or lossless (BSD).</summary>
public sealed class AvifencCompressor() : ToolCompressor("avifenc.exe")
{
    public override string Id => "avifenc";

    public override string DisplayName => "avifenc";

    public override bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        output == MediaFormat.Avif && WicImageCompressor.CanRead(input);

    public override async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var source = job.OutputPath + ".src.png";
        try
        {
            await IntermediatePng.CreateAsync(job, source, cancellationToken);

            var options = job.Options;
            var args = new List<string>
            {
                "--speed", options.Effort switch
                {
                    CompressionEffort.Fast => "8",
                    CompressionEffort.Maximum => "3",
                    _ => "6",
                },
                "--jobs", "all",
            };
            if (options.Mode == CompressionMode.Lossless) args.Add("--lossless");
            else args.AddRange(["-q", Math.Clamp(options.Quality, 0, 100).ToString()]);
            if (options.StripMetadata) args.AddRange(["--ignore-exif", "--ignore-xmp"]);

            args.AddRange([source, job.OutputPath]);
            await RunAsync(args, cancellationToken);
        }
        finally
        {
            File.Delete(source);
        }
    }
}
