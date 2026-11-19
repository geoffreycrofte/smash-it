using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Imaging;
using SmashIt.Core.Metadata;
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

/// <summary>
/// pngquant: lossy PNG compression (GPL-3.0). Reduces the image to a palette of at most 256 colors with
/// dithering, usually 60–80 % smaller; oxipng then polishes the result.
/// </summary>
/// <remarks>
/// pngquant converts pixels tagged with an ICC profile to sRGB. When the pipeline is about to write the
/// source profile back (<see cref="MetadataTransfer.PipelineHandles"/>), that would apply the profile twice, so
/// pngquant gets the pixels without any color tag and quantizes them in the source color space instead.
/// </remarks>
public sealed class PngquantCompressor() : ToolCompressor("pngquant.exe")
{
    /// <summary>pngquant's exit code when it can't reach the minimum quality: no file is written.</summary>
    private const int QualityTooLow = 99;

    private static readonly HashSet<string> ColorChunks = ["iCCP", "sRGB", "gAMA", "cHRM"];

    public override string Id => "pngquant";

    public override string DisplayName => "pngquant";

    public override bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        output == MediaFormat.Png && options.Mode == CompressionMode.Lossy && WicImageCompressor.CanRead(input);

    /// <summary>
    /// pngquant's --quality range for a Smash It quality: the target is the upper bound, and images that can't
    /// stay within 30 points of it are left to the lossless engines rather than visibly damaged.
    /// </summary>
    public static (int Min, int Max) QualityRange(int quality)
    {
        var max = Math.Clamp(quality, 1, 100);
        return (Math.Max(0, max - 30), max);
    }

    public override async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        var source = job.OutputPath + ".src.png";
        try
        {
            byte[] png;
            if (job.InputFormat == MediaFormat.Png)
            {
                png = await File.ReadAllBytesAsync(job.InputPath, cancellationToken);
                // A palette has no room for animation: pngquant would keep the first frame only.
                if (PngMetadataIO.HasChunk(png, "acTL"))
                    throw new NotSupportedException(L10n.T(
                        "Animated PNGs aren't supported yet.",
                        "Les PNG animés ne sont pas encore pris en charge."));
            }
            else
            {
                await IntermediatePng.CreateAsync(job, source, cancellationToken);
                png = await File.ReadAllBytesAsync(source, cancellationToken);
            }

            var profileRestored = MetadataTransfer.PipelineHandles(job.InputFormat, job.OutputFormat)
                && MetadataTransfer.Read(job.InputPath, job.InputFormat)?.Icc is not null;
            if (profileRestored) png = PngMetadataIO.RemoveChunks(png, ColorChunks);
            await File.WriteAllBytesAsync(source, png, cancellationToken);

            var (min, max) = QualityRange(job.Options.Quality);
            var speed = job.Options.Effort switch
            {
                CompressionEffort.Fast => "10",
                CompressionEffort.Maximum => "1",
                _ => "4",
            };
            // Metadata is written by the pipeline (or was already chosen in the intermediate PNG).
            var result = await ExternalTool.RunRawAsync(
                ToolPath ?? throw new ExternalToolException(L10n.T("pngquant not found.", "pngquant est introuvable.")),
                ["--quality", $"{min}-{max}", "--speed", speed, "--strip", "--force", "--output", job.OutputPath, "--", source],
                cancellationToken);

            if (result.ExitCode == QualityTooLow)
            {
                File.Delete(job.OutputPath); // no result: the lossless engines win
                return;
            }
            if (result.ExitCode != 0) throw ExternalTool.Failure(ToolPath, result);

            // pngquant tags its output as sRGB; the source profile replaces that tag.
            if (profileRestored)
            {
                var output = await File.ReadAllBytesAsync(job.OutputPath, cancellationToken);
                await File.WriteAllBytesAsync(job.OutputPath, PngMetadataIO.RemoveChunks(output, ColorChunks), cancellationToken);
            }
        }
        finally
        {
            File.Delete(source);
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
