using NetVips;
using SmashIt.Core.Formats;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Imaging;

/// <summary>
/// <see cref="IImageCodec"/> over libvips (NetVips), for macOS and Linux. libvips reads JPEG, PNG, GIF, WebP, AVIF and
/// TIFF; HEIC goes through the bundled ffmpeg, since the libvips build has no HEVC decoder (patents).
/// </summary>
internal sealed class VipsImageCodec : IImageCodec
{
    private static readonly HashSet<MediaFormat> Readable =
    [
        MediaFormat.Jpeg, MediaFormat.Png, MediaFormat.Gif, MediaFormat.WebP, MediaFormat.Avif, MediaFormat.Tiff,
    ];

    private readonly string? _ffmpeg = ExternalTool.Find("ffmpeg");

    public bool CanRead(MediaFormat format) => Readable.Contains(format) || (format == MediaFormat.Heif && _ffmpeg is not null);

    public async Task<(uint Width, uint Height)?> ReadSizeAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var decoded = await OpenAsync(path, cancellationToken);
            return ((uint)decoded.Image.Width, (uint)decoded.Image.Height);
        }
        catch (Exception ex) when (ex is VipsException or ExternalToolException or IOException)
        {
            return null;
        }
    }

    public async Task<uint> ReadFrameCountAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            using var decoded = await OpenAsync(path, cancellationToken);
            return (uint)Math.Max(1, PageCount(decoded.Image));
        }
        catch (Exception ex) when (ex is VipsException or ExternalToolException or IOException)
        {
            return 0;
        }
    }

    public async Task<uint> WritePngAsync(string input, string output, PngConversion conversion, CancellationToken cancellationToken)
    {
        using var decoded = await OpenAsync(input, cancellationToken);
        var frames = (uint)Math.Max(1, PageCount(decoded.Image));
        await Task.Run(() =>
        {
            using var image = Prepare(decoded.Image, conversion);
            image.Pngsave(
                output,
                compression: conversion.Fast ? 1 : 6,
                filter: conversion.Fast ? Enums.ForeignPngFilter.None : null,
                keep: Enums.ForeignKeep.None);
        }, cancellationToken);
        return frames;
    }

    public Task ResizeJpegAsync(
        string input, string output, uint width, uint height, bool grayscale, int quality, CancellationToken cancellationToken) =>
        Task.Run(() =>
        {
            // As stored: no orientation, no color management (the PDF says how to read the colors).
            using var source = Image.NewFromFile(input, access: Enums.Access.Sequential);
            using var scaled = Scale(source, width, height);
            using var channels = grayscale && scaled.Bands > 1 ? scaled.Colourspace(Enums.Interpretation.Bw) : scaled.Copy();
            channels.Jpegsave(output, q: Math.Clamp(quality, 1, 100), keep: Enums.ForeignKeep.None);
        }, cancellationToken);

    /// <summary>Applies orientation, color conversion and scaling, and brings the pixels to 8 bits per channel.</summary>
    internal static Image Prepare(Image source, PngConversion conversion)
    {
        var image = source.Copy();
        try
        {
            // Scaled as stored, then rotated, like WIC: the scale applies to the stored frame.
            if (conversion.Scale is { } scale)
            {
                var (width, height) = ScaledSize((uint)image.Width, (uint)image.Height, scale);
                Replace(ref image, Scale(image, width, height));
            }

            if (conversion.ApplyOrientation) Replace(ref image, image.Autorot());

            var hasProfile = image.Contains("icc-profile-data");
            if (conversion.ConvertToSrgb && hasProfile)
                Replace(ref image, image.IccTransform("srgb", embedded: true, depth: 8));
            else if (image.Interpretation is Enums.Interpretation.Cmyk or Enums.Interpretation.Lab or Enums.Interpretation.Labs
                     || conversion.ConvertToSrgb)
                Replace(ref image, image.Colourspace(Enums.Interpretation.Srgb));

            // 16-bit sources: 8 bits per channel, like WIC's BGRA8.
            if (image.Format != Enums.BandFormat.Uchar)
            {
                var target = image.Bands <= 2 ? Enums.Interpretation.Bw : Enums.Interpretation.Srgb;
                Replace(ref image, image.Colourspace(target));
                if (image.Format != Enums.BandFormat.Uchar) Replace(ref image, image.Cast(Enums.BandFormat.Uchar));
            }
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    /// <summary>Scales to exactly <paramref name="width"/>×<paramref name="height"/>, alpha-aware.</summary>
    private static Image Scale(Image image, uint width, uint height)
    {
        var horizontal = (double)width / image.Width;
        var vertical = (double)height / image.Height;
        if (!image.HasAlpha()) return image.Resize(horizontal, vscale: vertical, kernel: Enums.Kernel.Lanczos3);

        // Transparent pixels must not bleed their color into their neighbours.
        var format = image.Format;
        using var premultiplied = image.Premultiply();
        using var resized = premultiplied.Resize(horizontal, vscale: vertical, kernel: Enums.Kernel.Lanczos3);
        using var unpremultiplied = resized.Unpremultiply();
        return unpremultiplied.Cast(format);
    }

    private static (uint Width, uint Height) ScaledSize(uint width, uint height, double scale) =>
        ((uint)Math.Max(1, Math.Round(width * scale)), (uint)Math.Max(1, Math.Round(height * scale)));

    private static void Replace(ref Image image, Image next)
    {
        image.Dispose();
        image = next;
    }

    internal static int PageCount(Image image) => image.Contains("n-pages") ? (int)image.Get("n-pages") : 1;

    /// <summary>Opens the first frame; HEIC is first decoded to a temporary PNG by ffmpeg.</summary>
    internal async Task<Decoded> OpenAsync(string path, CancellationToken cancellationToken)
    {
        if (FormatDetector.DetectFile(path) != MediaFormat.Heif)
            return new Decoded(Image.NewFromFile(path, access: Enums.Access.Random), null);

        if (_ffmpeg is null)
            throw new ExternalToolException(L10n.T("ffmpeg is needed to read HEIC images.", "ffmpeg est nécessaire pour lire les images HEIC."));
        var png = Path.Combine(Path.GetTempPath(), $"smashit-{Guid.NewGuid():N}.png");
        try
        {
            // The primary image, oriented (HEIF stores rotation and mirroring apart from the pixels).
            await ExternalTool.RunAsync(
                _ffmpeg,
                ["-hide_banner", "-v", "error", "-i", path, "-map", "0:v:0", "-frames:v", "1", "-c:v", "png", "-f", "image2", "-y", png],
                cancellationToken);
            return new Decoded(Image.NewFromFile(png, memory: true), png);
        }
        catch
        {
            File.Delete(png);
            throw;
        }
    }

    /// <summary>A decoded image, and the temporary file it came from (removed with it).</summary>
    internal sealed record Decoded(Image Image, string? TemporaryFile) : IDisposable
    {
        public void Dispose()
        {
            Image.Dispose();
            if (TemporaryFile is not null) File.Delete(TemporaryFile);
        }
    }
}
