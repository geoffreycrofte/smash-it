using SmashIt.Core.Formats;

namespace SmashIt.Core.Imaging;

/// <summary>
/// Reads and writes pixels for the engines that need them (sizes, frame counts, the PNG handed to cwebp, avifenc and
/// pngquant, a PDF's resampled JPEGs). One implementation per system: <see cref="Platform.ImageCodec"/>.
/// </summary>
internal interface IImageCodec
{
    /// <summary>Whether this codec can decode <paramref name="format"/>.</summary>
    bool CanRead(MediaFormat format);

    /// <summary>The pixel size of an image as stored, or <c>null</c> when it can't be read.</summary>
    Task<(uint Width, uint Height)?> ReadSizeAsync(string path, CancellationToken cancellationToken);

    /// <summary>The number of frames (several for an animated GIF), or 0 when the image can't be read.</summary>
    Task<uint> ReadFrameCountAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Decodes the first frame of <paramref name="input"/> and writes it to <paramref name="output"/> as an 8-bit
    /// PNG with straight alpha. Returns the number of frames of the source.
    /// </summary>
    Task<uint> WritePngAsync(string input, string output, PngConversion conversion, CancellationToken cancellationToken);

    /// <summary>
    /// Scales a JPEG to <paramref name="width"/>×<paramref name="height"/> and re-encodes it, keeping its colors as
    /// they are (no color management).
    /// </summary>
    /// <param name="grayscale">One channel in, one channel out (a PDF's DeviceGray must stay one channel).</param>
    Task ResizeJpegAsync(
        string input, string output, uint width, uint height, bool grayscale, int quality, CancellationToken cancellationToken);
}

/// <summary>How <see cref="IImageCodec.WritePngAsync"/> turns an image into a PNG.</summary>
/// <param name="ApplyOrientation">Rotate/flip the pixels according to the EXIF orientation.</param>
/// <param name="ConvertToSrgb">Convert colors from the embedded profile to sRGB.</param>
/// <param name="Scale">Scales both dimensions down (0–1) with area averaging; <c>null</c> keeps the size.</param>
/// <param name="Fast">A temporary file: encoding speed matters more than size.</param>
internal sealed record PngConversion(bool ApplyOrientation, bool ConvertToSrgb, double? Scale = null, bool Fast = false);
