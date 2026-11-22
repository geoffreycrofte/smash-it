using SmashIt.Core.Compression;
using SmashIt.Core.Compressors;
using SmashIt.Core.Imaging;

namespace SmashIt.Core;

/// <summary>
/// What differs from one system to another, for macOS and Linux: libvips reads and writes the images. No trash yet.
/// The Windows version lives in Platforms/Windows; the project compiles one or the other.
/// </summary>
internal static class Platform
{
    private static readonly VipsImageCodec Vips = new();

    public static IImageCodec ImageCodec => Vips;

    /// <summary>Image engines built into the system, tried before the bundled tools.</summary>
    public static ICompressor[] ImageEngines() => [new VipsImageCompressor(Vips)];

    /// <summary>Video engines built into the system, tried before ffmpeg.</summary>
    public static ICompressor[] VideoEngines() => [];

    public static void MoveToTrash(string path) =>
        throw new PlatformNotSupportedException(L10n.T(
            "Moving files to the trash isn't supported on this system yet.",
            "La mise à la corbeille n'est pas encore prise en charge sur ce système."));
}
