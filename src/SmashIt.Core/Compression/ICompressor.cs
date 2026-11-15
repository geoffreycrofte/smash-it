using SmashIt.Core.Formats;

namespace SmashIt.Core.Compression;

/// <summary>One unit of work handed to a compressor.</summary>
public sealed record CompressionJob(
    string InputPath,
    MediaFormat InputFormat,
    string OutputPath,
    MediaFormat OutputFormat,
    CompressionOptions Options)
{
    /// <summary>Progress from 0 to 100, reported by long-running engines (video).</summary>
    public IProgress<double>? Progress { get; init; }
}

/// <summary>
/// A compression engine (WIC, oxipng, jpegtran, a custom script…).
/// The pipeline asks every registered compressor whether it can handle a job and keeps the smallest result.
/// </summary>
/// <remarks>
/// Metadata contract: when <see cref="Metadata.MetadataTransfer.PipelineHandles"/> is true for a job, the
/// compressor must keep the source pixels as they are (no color conversion, orientation applied only when
/// <see cref="Metadata.MetadataTransfer.BakesOrientation"/> says so). The pipeline then writes the metadata
/// the user asked for. Otherwise the compressor is responsible for metadata itself.
/// </remarks>
public interface ICompressor
{
    /// <summary>Stable identifier, e.g. <c>wic</c>, <c>oxipng</c>.</summary>
    string Id { get; }

    string DisplayName { get; }

    /// <summary>False when the engine is missing (e.g. a bundled tool was not found).</summary>
    bool IsAvailable => true;

    /// <summary>
    /// Lossless optimizers (oxipng, jpegtran…) also run on the output of the other engines,
    /// e.g. jpegtran after a lossy JPEG encode.
    /// </summary>
    bool IsOptimizer => false;

    bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options);

    /// <summary>Reads <see cref="CompressionJob.InputPath"/> and writes <see cref="CompressionJob.OutputPath"/>. Never touches the input.</summary>
    Task CompressAsync(CompressionJob job, CancellationToken cancellationToken);
}
