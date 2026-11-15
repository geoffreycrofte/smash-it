using SmashIt.Core.Formats;

namespace SmashIt.Core.Compression;

/// <summary>
/// A compression engine (WIC, Media Foundation, oxipng, a custom script…).
/// The pipeline asks every registered compressor whether it can handle a job and keeps the smallest result.
/// </summary>
public interface ICompressor
{
    /// <summary>Stable identifier, e.g. <c>wic</c>, <c>oxipng</c>.</summary>
    string Id { get; }

    string DisplayName { get; }

    bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options);

    /// <summary>Reads <paramref name="inputPath"/> and writes a new file at <paramref name="outputPath"/>. Never touches the input.</summary>
    Task CompressAsync(
        string inputPath,
        string outputPath,
        MediaFormat outputFormat,
        CompressionOptions options,
        CancellationToken cancellationToken);
}
