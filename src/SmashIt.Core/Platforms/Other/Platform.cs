using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Imaging;
using SmashIt.Core.IO;

namespace SmashIt.Core;

/// <summary>
/// What differs from one system to another, for macOS and Linux: libvips reads and writes the images, and the
/// originals go to the Finder's or the desktop's trash. The Windows version lives in Platforms/Windows; the project
/// compiles one or the other.
/// </summary>
internal static class Platform
{
    private static readonly VipsImageCodec Vips = new();

    public static IImageCodec ImageCodec => Vips;

    /// <summary>Image engines built into the system, tried before the bundled tools.</summary>
    public static ICompressor[] ImageEngines() => [new VipsImageCompressor(Vips)];

    /// <summary>Video engines built into the system, tried before ffmpeg.</summary>
    public static ICompressor[] VideoEngines() => [];

    public static void MoveToTrash(string path) => Trash.Move(path);
}
