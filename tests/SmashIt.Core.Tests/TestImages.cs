using SmashIt.Core.Formats;
using SmashIt.Core.Metadata;
#if WINDOWS
using Windows.Graphics.Imaging;
#else
using NetVips;
#endif

namespace SmashIt.Core.Tests;

/// <summary>Builds real images for the tests: with WIC on Windows, with libvips elsewhere.</summary>
internal static class TestImages
{
    /// <summary>A recognizable fake ICC profile: content is opaque to the metadata code.</summary>
    public static readonly byte[] FakeIcc = Enumerable.Range(0, 3000).Select(i => (byte)(i * 7)).ToArray();

    public static readonly byte[] Xmp = "<x:xmpmeta xmlns:x=\"adobe:ns:meta/\"><rdf:RDF/></x:xmpmeta>"u8.ToArray();

    /// <summary>EXIF with orientation + one extra tag (Make, ASCII, inline "ABC").</summary>
    public static byte[] Exif(int orientation)
    {
        var tiff = new byte[38];
        "II"u8.CopyTo(tiff);
        BitConverter.TryWriteBytes(tiff.AsSpan(2), (ushort)42);
        BitConverter.TryWriteBytes(tiff.AsSpan(4), 8u);
        BitConverter.TryWriteBytes(tiff.AsSpan(8), (ushort)2);
        // Make (0x010F), ASCII, 4 bytes inline
        BitConverter.TryWriteBytes(tiff.AsSpan(10), (ushort)0x010F);
        BitConverter.TryWriteBytes(tiff.AsSpan(12), (ushort)2);
        BitConverter.TryWriteBytes(tiff.AsSpan(14), 4u);
        "ABC\0"u8.CopyTo(tiff.AsSpan(18));
        // Orientation
        BitConverter.TryWriteBytes(tiff.AsSpan(22), (ushort)0x0112);
        BitConverter.TryWriteBytes(tiff.AsSpan(24), (ushort)3);
        BitConverter.TryWriteBytes(tiff.AsSpan(26), 1u);
        BitConverter.TryWriteBytes(tiff.AsSpan(30), (ushort)orientation);
        return tiff;
    }

    /// <param name="noisy">Random blue channel (hard to compress); off for a photo-like gradient with light grain.</param>
    public static async Task<string> WriteAsync(
        string path, MediaFormat format, uint width, uint height, ImageMetadata? metadata = null, bool noisy = true)
    {
        var pixels = new byte[width * height * 4];
        var random = new Random(42);
        for (var y = 0; y < height; y++)
        for (var x = 0; x < width; x++)
        {
            var i = (y * (int)width + x) * 4;
            var grain = noisy ? 0 : random.Next(-6, 7);
            pixels[i] = (byte)Math.Clamp(x * 255 / Math.Max(1, width - 1) + grain, 0, 255);
            pixels[i + 1] = (byte)Math.Clamp(y * 255 / Math.Max(1, height - 1) + grain, 0, 255);
            pixels[i + 2] = noisy ? (byte)random.Next(256) : (byte)Math.Clamp((x + y) * 255 / Math.Max(1, width + height - 2) + grain, 0, 255);
            pixels[i + 3] = 255;
        }

        await EncodeAsync(path, format, width, height, pixels);
        if (metadata is not null) MetadataTransfer.Write(path, format, metadata);
        return path;
    }

    public static async Task<(uint Width, uint Height)> SizeAsync(string path) =>
        await Platform.ImageCodec.ReadSizeAsync(path, CancellationToken.None)
        ?? throw new InvalidDataException($"Image illisible : {path}");

#if WINDOWS
    /// <param name="bgra">8-bit BGRA pixels; alpha is ignored.</param>
    private static async Task EncodeAsync(string path, MediaFormat format, uint width, uint height, byte[] bgra)
    {
        var encoderId = format switch
        {
            MediaFormat.Jpeg => BitmapEncoder.JpegEncoderId,
            MediaFormat.Gif => BitmapEncoder.GifEncoderId,
            _ => BitmapEncoder.PngEncoderId,
        };
        await using var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
        using var stream = file.AsRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, bgra);
        await encoder.FlushAsync();
    }
#else
    /// <param name="bgra">8-bit BGRA pixels; alpha is ignored.</param>
    private static Task EncodeAsync(string path, MediaFormat format, uint width, uint height, byte[] bgra)
    {
        var rgb = new byte[width * height * 3];
        for (int i = 0, o = 0; i < bgra.Length; i += 4, o += 3)
        {
            rgb[o] = bgra[i + 2];
            rgb[o + 1] = bgra[i + 1];
            rgb[o + 2] = bgra[i];
        }
        using var raw = Image.NewFromMemory(rgb, (int)width, (int)height, 3, Enums.BandFormat.Uchar);
        using var image = raw.Copy(interpretation: Enums.Interpretation.Srgb, xres: 96 / 25.4, yres: 96 / 25.4);
        switch (format)
        {
            case MediaFormat.Jpeg: image.Jpegsave(path, q: 90); break; // WIC's default quality
            case MediaFormat.Gif: image.Gifsave(path); break;
            default: image.Pngsave(path); break;
        }
        return Task.CompletedTask;
    }
#endif
}
