using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;

namespace SmashIt.Core;

public static class SmashItEngine
{
    /// <summary>The pipeline with every built-in compressor registered.</summary>
    public static CompressionPipeline CreateDefaultPipeline() => new(
    [
        new WicImageCompressor(),
    ]);
}
