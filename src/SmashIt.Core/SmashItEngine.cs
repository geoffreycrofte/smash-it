using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;

namespace SmashIt.Core;

public static class SmashItEngine
{
    /// <summary>The pipeline with every built-in compressor registered. Missing bundled tools are skipped.</summary>
    public static CompressionPipeline CreateDefaultPipeline() => new(
    [
        new WicImageCompressor(),
        new JpegtranCompressor(),
        new OxipngCompressor(),
        new PngquantCompressor(),
        new GifsicleCompressor(),
        new OxvgCompressor(),
        new CwebpCompressor(),
        new AvifencCompressor(),
        new PdfCompressor(),
        new VideoCompressor(),
    ]);
}
