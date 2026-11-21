using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;

namespace SmashIt.Core;

public static class SmashItEngine
{
    /// <summary>The pipeline with every built-in compressor registered. Missing bundled tools are skipped.</summary>
    public static CompressionPipeline CreateDefaultPipeline()
    {
        List<ICompressor> engines =
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
            new FfmpegVideoCompressor(),
            new FfmpegGifVideoCompressor(),
            new FfmpegAudioCompressor(),
        ];
        // Presentations hand the pictures, videos and sounds they hold to the other engines.
        return new CompressionPipeline([.. engines, new PresentationCompressor(new CompressionPipeline(engines))]);
    }
}
