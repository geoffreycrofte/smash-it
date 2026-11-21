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
        var format = Detect(buffer[..read]);

        // What the first bytes can't tell: an MP4 holding only sound (its brand is often a generic "mp42"), and an ID3
        // tag, which usually starts an MP3 but can sit in front of raw AAC or FLAC. The extension decides.
        var byExtension = MediaFormatInfo.FromExtension(path);
        return format switch
        {
            MediaFormat.Mp4 when byExtension == MediaFormat.M4a => MediaFormat.M4a,
            MediaFormat.Mp3 when buffer.StartsWith("ID3"u8) && byExtension is MediaFormat.Aac or MediaFormat.Flac => byExtension,
            _ => format,
        };
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
            if (type.SequenceEqual("WAVE"u8)) return MediaFormat.Wav;
        }

        // Sound
        if (h.StartsWith("fLaC"u8)) return MediaFormat.Flac;
        if (h.StartsWith("OggS"u8)) return h.IndexOf("OpusHead"u8) >= 0 ? MediaFormat.Opus : MediaFormat.Ogg;
        if (h.Length >= 12 && h.StartsWith("FORM"u8) && (h.Slice(8, 4).SequenceEqual("AIFF"u8) || h.Slice(8, 4).SequenceEqual("AIFC"u8)))
            return MediaFormat.Aiff;
        if (h.StartsWith("ID3"u8)) return MediaFormat.Mp3;

        if (h.Length >= 12 && h.Slice(4, 4).SequenceEqual("ftyp"u8)) return DetectIsoBmff(h);

        if (h.StartsWith((ReadOnlySpan<byte>)[0x1A, 0x45, 0xDF, 0xA3]))
            return h.IndexOf("webm"u8) >= 0 ? MediaFormat.WebM : MediaFormat.Mkv;

        if (LooksLikeSvg(h)) return MediaFormat.Svg;

        return DetectMpegAudioFrame(h);
    }

    /// <summary>
    /// MP3 or raw AAC (ADTS) without a tag: the file starts with a frame, 11 set bits then fields that must hold valid
    /// values (a UTF-16 text file also starts with 0xFF 0xFE). Layer bits 00 mean AAC, anything else MP3.
    /// </summary>
    private static MediaFormat DetectMpegAudioFrame(ReadOnlySpan<byte> h)
    {
        if (h.Length < 4 || h[0] != 0xFF || (h[1] & 0xE0) != 0xE0) return MediaFormat.Unknown;
        if ((h[1] & 0x06) == 0)
            return (h[1] & 0xF0) == 0xF0 && ((h[2] >> 2) & 0xF) < 13 ? MediaFormat.Aac : MediaFormat.Unknown;
        var validVersion = (h[1] & 0x18) != 0x08;
        var validBitrate = h[2] >> 4 is not 0xF;
        var validSampleRate = ((h[2] >> 2) & 0x3) != 0x3;
        return validVersion && validBitrate && validSampleRate ? MediaFormat.Mp3 : MediaFormat.Unknown;
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
        if (brands[0] is "M4A " or "M4B " or "M4P ") return MediaFormat.M4a;
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
