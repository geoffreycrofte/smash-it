using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Imaging;

namespace SmashIt.Core;

/// <summary>
/// What differs from one system to another, for macOS and Linux. Not finished yet: no image codec (libvips will
/// fill it) and no trash, so only the engines that need neither work. The Windows version lives in
/// Platforms/Windows; the project compiles one or the other.
/// </summary>
internal static class Platform
{
    public static IImageCodec ImageCodec { get; } = new NoImageCodec();

    /// <summary>Image engines built into the system, tried before the bundled tools.</summary>
    public static ICompressor[] ImageEngines() => [];

    /// <summary>Video engines built into the system, tried before ffmpeg.</summary>
    public static ICompressor[] VideoEngines() => [];

    public static void MoveToTrash(string path) =>
        throw new PlatformNotSupportedException(L10n.T(
            "Moving files to the trash isn't supported on this system yet.",
            "La mise à la corbeille n'est pas encore prise en charge sur ce système."));

    /// <summary>Reads nothing: the engines that need pixels stay out of the pipeline.</summary>
    private sealed class NoImageCodec : IImageCodec
    {
        public bool CanRead(MediaFormat format) => false;

        public Task<(uint Width, uint Height)?> ReadSizeAsync(string path, CancellationToken cancellationToken) =>
            Task.FromResult<(uint, uint)?>(null);

        public Task<uint> ReadFrameCountAsync(string path, CancellationToken cancellationToken) => Task.FromResult(0u);

        public Task<uint> WritePngAsync(string input, string output, PngConversion conversion, CancellationToken cancellationToken) =>
            throw Unsupported();

        public Task ResizeJpegAsync(
            string input, string output, uint width, uint height, bool grayscale, int quality, CancellationToken cancellationToken) =>
            throw Unsupported();

        private static PlatformNotSupportedException Unsupported() => new(L10n.T(
            "Decoding images isn't supported on this system yet.",
            "Le décodage d'images n'est pas encore pris en charge sur ce système."));
    }
}
