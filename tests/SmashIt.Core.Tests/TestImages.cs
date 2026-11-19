using SmashIt.Core.Formats;
using SmashIt.Core.Metadata;
using Windows.Graphics.Imaging;

namespace SmashIt.Core.Tests;

/// <summary>Builds real images with WIC for the tests.</summary>
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

        var encoderId = format == MediaFormat.Jpeg ? BitmapEncoder.JpegEncoderId : BitmapEncoder.PngEncoderId;
        await using (var file = new FileStream(path, FileMode.Create, FileAccess.ReadWrite))
        {
            using var stream = file.AsRandomAccessStream();
            var encoder = await BitmapEncoder.CreateAsync(encoderId, stream);
            encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Ignore, width, height, 96, 96, pixels);
            await encoder.FlushAsync();
        }

        if (metadata is not null) MetadataTransfer.Write(path, format, metadata);
        return path;
    }

    public static async Task<(uint Width, uint Height)> SizeAsync(string path)
    {
        await using var file = new FileStream(path, FileMode.Open, FileAccess.Read);
        using var stream = file.AsRandomAccessStream();
        var decoder = await BitmapDecoder.CreateAsync(stream);
        return (decoder.PixelWidth, decoder.PixelHeight);
    }
}
