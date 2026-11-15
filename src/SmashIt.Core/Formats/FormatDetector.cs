using System.Buffers.Binary;
using System.Text;

namespace SmashIt.Core.Formats;

/// <summary>Identifies a file's real format from its first bytes (magic numbers), not its extension.</summary>
public static class FormatDetector
{
    public const int HeaderLength = 512;

    public static MediaFormat DetectFile(string path)
    {
        Span<byte> buffer = stackalloc byte[HeaderLength];
        using var stream = File.OpenRead(path);
        var read = stream.ReadAtLeast(buffer, HeaderLength, throwOnEndOfStream: false);
        return Detect(buffer[..read]);
    }

    public static MediaFormat Detect(ReadOnlySpan<byte> h)
    {
        if (h.StartsWith((ReadOnlySpan<byte>)[0xFF, 0xD8, 0xFF])) return MediaFormat.Jpeg;
        if (h.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A])) return MediaFormat.Png;
        if (h.StartsWith("GIF87a"u8) || h.StartsWith("GIF89a"u8)) return MediaFormat.Gif;
        if (h.StartsWith("%PDF-"u8)) return MediaFormat.Pdf;
        if (h.StartsWith((ReadOnlySpan<byte>)[0x49, 0x49, 0xBC])) return MediaFormat.JpegXr;
        if (h.StartsWith((ReadOnlySpan<byte>)[0x49, 0x49, 0x2A, 0x00]) ||
            h.StartsWith((ReadOnlySpan<byte>)[0x4D, 0x4D, 0x00, 0x2A])) return MediaFormat.Tiff;
        if (h.Length >= 14 && h.StartsWith("BM"u8)) return MediaFormat.Bmp;

        if (h.Length >= 12 && h.StartsWith("RIFF"u8))
        {
            var type = h.Slice(8, 4);
            if (type.SequenceEqual("WEBP"u8)) return MediaFormat.WebP;
            if (type.SequenceEqual("AVI "u8)) return MediaFormat.Avi;
        }

        if (h.Length >= 12 && h.Slice(4, 4).SequenceEqual("ftyp"u8)) return DetectIsoBmff(h);

        if (h.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
            return h.IndexOf("webm"u8) >= 0 ? MediaFormat.WebM : MediaFormat.Mkv;

        if (LooksLikeSvg(h)) return MediaFormat.Svg;

        return MediaFormat.Unknown;
    }

    /// <summary>MP4 / MOV / HEIF / AVIF all share the ISO base media "ftyp" box; the brands tell them apart.</summary>
    private static MediaFormat DetectIsoBmff(ReadOnlySpan<byte> h)
    {
        var boxSize = (int)Math.Min(BinaryPrimitives.ReadUInt32BigEndian(h), (uint)h.Length);
        var brands = new List<string> { Encoding.ASCII.GetString(h.Slice(8, 4)) };
        for (var offset = 16; offset + 4 <= boxSize; offset += 4)
            brands.Add(Encoding.ASCII.GetString(h.Slice(offset, 4)));

        if (brands.Any(b => b is "avif" or "avis")) return MediaFormat.Avif;
        if (brands.Any(b => b is "heic" or "heix" or "hevc" or "hevx" or "heim" or "heis" or "mif1" or "msf1"))
            return MediaFormat.Heif;
        if (brands[0] == "qt  ") return MediaFormat.Mov;
        return MediaFormat.Mp4;
    }

    private static bool LooksLikeSvg(ReadOnlySpan<byte> h)
    {
        var text = Encoding.UTF8.GetString(h).TrimStart('﻿', ' ', '\t', '\r', '\n');
        return (text.StartsWith("<?xml", StringComparison.Ordinal) || text.StartsWith("<svg", StringComparison.Ordinal)
                || text.StartsWith("<!DOCTYPE svg", StringComparison.OrdinalIgnoreCase))
               && text.Contains("<svg", StringComparison.Ordinal);
    }
}
