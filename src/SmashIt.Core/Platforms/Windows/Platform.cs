using Microsoft.VisualBasic.FileIO;
using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Imaging;

namespace SmashIt.Core;

/// <summary>
/// What differs from one system to another. Windows: WIC, Media Foundation and the Recycle Bin. The other systems'
/// version lives in Platforms/Other; the project compiles one or the other.
/// </summary>
internal static class Platform
{
    public static IImageCodec ImageCodec { get; } = new WicImageCodec();

    /// <summary>Image engines built into the system, tried before the bundled tools.</summary>
    public static ICompressor[] ImageEngines() => [new WicImageCompressor()];

    /// <summary>Video engines built into the system, tried before ffmpeg.</summary>
    public static ICompressor[] VideoEngines() => [new VideoCompressor()];

    public static void MoveToTrash(string path) =>
        FileSystem.DeleteFile(path, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
}
