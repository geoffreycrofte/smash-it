using System.Buffers.Binary;
using System.Text;
using SmashIt.Core.Formats;

namespace SmashIt.Core.Metadata;

/// <summary>
/// Reads EXIF, XMP and the ICC profile from WebP (RIFF) and HEIF files (HEIC, AVIF: ISO base media file format).
/// Read only: those formats are re-encoded by other tools, which receive the metadata through the intermediate PNG.
/// </summary>
public static class ContainerMetadataIO
{
    public static bool CanRead(MediaFormat format) => format is MediaFormat.WebP or MediaFormat.Heif or MediaFormat.Avif;

    /// <summary>The metadata of a WebP, HEIC or AVIF file; <c>null</c> when it has none or can't be read.</summary>
    public static ImageMetadata? Read(byte[] file, MediaFormat format)
    {
        try
        {
            var (exif, xmp, icc) = format == MediaFormat.WebP ? ReadWebP(file) : ReadHeif(file);
            var metadata = new ImageMetadata { SourceFormat = format, Exif = TiffOnly(exif), Xmp = xmp, Icc = icc };
            return metadata.IsEmpty ? null : metadata;
        }
        catch (Exception ex) when (ex is ArgumentOutOfRangeException or IndexOutOfRangeException or InvalidDataException or OverflowException)
        {
            return null; // truncated or unusual file: no metadata rather than a failure
        }
    }

    /// <summary>EXIF as a TIFF structure: some writers prefix it with "Exif\0\0", or with a 4-byte offset (HEIF).</summary>
    private static byte[]? TiffOnly(byte[]? exif)
    {
        if (exif is null) return null;
        for (var i = 0; i + 4 <= exif.Length && i <= 16; i++)
        {
            var span = exif.AsSpan(i);
            if (span.StartsWith("II*\0"u8) || span.StartsWith("MM\0*"u8)) return exif[i..];
        }
        return null;
    }

    // WebP: "RIFF" size "WEBP", then chunks: four-character code, little-endian size, data, padding to even.
    private static (byte[]? Exif, byte[]? Xmp, byte[]? Icc) ReadWebP(byte[] file)
    {
        if (file.Length < 12 || !file.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !file.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidDataException("Not a WebP file.");
        byte[]? exif = null, xmp = null, icc = null;
        for (var pos = 12; pos + 8 <= file.Length;)
        {
            var type = Encoding.ASCII.GetString(file, pos, 4);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(pos + 4));
            if (size < 0 || pos + 8 + size > file.Length) break;
            var data = file.AsSpan(pos + 8, size).ToArray();
            switch (type)
            {
                case "EXIF": exif ??= data; break;
                case "XMP ": xmp ??= data; break;
                case "ICCP": icc ??= data; break;
            }
            pos += 8 + size + (size & 1);
        }
        return (exif, xmp, icc);
    }

    /// <summary>
    /// Adds EXIF and XMP to a WebP file (replacing any already there), turning a simple WebP into the extended
    /// format that can hold them. cwebp can't do it on Windows: it only reads the ICC profile of a PNG.
    /// </summary>
    public static byte[] WriteWebP(byte[] file, byte[]? exif, byte[]? xmp)
    {
        if (file.Length < 12 || !file.AsSpan(0, 4).SequenceEqual("RIFF"u8) || !file.AsSpan(8, 4).SequenceEqual("WEBP"u8))
            throw new InvalidDataException("Not a WebP file.");

        var chunks = new List<(string Type, byte[] Data)>();
        for (var pos = 12; pos + 8 <= file.Length;)
        {
            var type = Encoding.ASCII.GetString(file, pos, 4);
            var size = (int)BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(pos + 4));
            if (size < 0 || pos + 8 + size > file.Length) throw new InvalidDataException("Truncated WebP chunk.");
            if (type is not ("EXIF" or "XMP ")) chunks.Add((type, file.AsSpan(pos + 8, size).ToArray()));
            pos += 8 + size + (size & 1);
        }
        if (chunks.Count == 0) throw new InvalidDataException("Empty WebP file.");

        // Extended format: a VP8X chunk first, whose flags say which optional chunks follow.
        if (chunks[0].Type != "VP8X") chunks.Insert(0, ("VP8X", ExtendedHeader(chunks[0])));
        var header = chunks[0].Data;
        const byte IccFlag = 0x20, ExifFlag = 0x08, XmpFlag = 0x04;
        header[0] = (byte)(header[0] & ~(ExifFlag | XmpFlag) | (exif is null ? 0 : ExifFlag) | (xmp is null ? 0 : XmpFlag));
        if (chunks.Any(c => c.Type == "ICCP")) header[0] |= IccFlag;

        // The spec puts EXIF then XMP after the image data.
        if (exif is not null) chunks.Add(("EXIF", exif));
        if (xmp is not null) chunks.Add(("XMP ", xmp));

        using var output = new MemoryStream();
        output.Write("RIFF\0\0\0\0WEBP"u8);
        Span<byte> size4 = stackalloc byte[4];
        foreach (var (type, data) in chunks)
        {
            output.Write(Encoding.ASCII.GetBytes(type));
            BinaryPrimitives.WriteUInt32LittleEndian(size4, (uint)data.Length);
            output.Write(size4);
            output.Write(data);
            if ((data.Length & 1) == 1) output.WriteByte(0);
        }
        var result = output.ToArray();
        BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(4), (uint)(result.Length - 8));
        return result;
    }

    /// <summary>A VP8X header (flags, canvas size) for a simple lossy (VP8) or lossless (VP8L) WebP.</summary>
    private static byte[] ExtendedHeader((string Type, byte[] Data) image)
    {
        int width, height;
        var alpha = false;
        var data = image.Data;
        switch (image.Type)
        {
            case "VP8 " when data.Length >= 10 && data[3] == 0x9D && data[4] == 0x01 && data[5] == 0x2A:
                width = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(6)) & 0x3FFF;
                height = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8)) & 0x3FFF;
                break;
            case "VP8L" when data.Length >= 5 && data[0] == 0x2F:
                var bits = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(1));
                width = (int)(bits & 0x3FFF) + 1;
                height = (int)((bits >> 14) & 0x3FFF) + 1;
                alpha = ((bits >> 28) & 1) == 1;
                break;
            default:
                throw new InvalidDataException("Unknown WebP image chunk.");
        }
        var header = new byte[10];
        header[0] = alpha ? (byte)0x10 : (byte)0;
        WriteUInt24(header.AsSpan(4), width - 1);
        WriteUInt24(header.AsSpan(7), height - 1);
        return header;
    }

    private static void WriteUInt24(Span<byte> span, int value)
    {
        span[0] = (byte)value;
        span[1] = (byte)(value >> 8);
        span[2] = (byte)(value >> 16);
    }

    // HEIF: boxes (32-bit size, type, 64-bit size when size == 1). The "meta" box lists items (iinf), where they
    // are (iloc), and the image properties (iprp/ipco, where "colr" holds the ICC profile).
    private static (byte[]? Exif, byte[]? Xmp, byte[]? Icc) ReadHeif(byte[] file)
    {
        var meta = Boxes(file, 0, file.Length).FirstOrDefault(b => b.Type == "meta");
        if (meta.Type is null) throw new InvalidDataException("No meta box.");
        var children = Boxes(file, meta.Start + 4, meta.End).ToList(); // "meta" is a full box: skip version/flags

        var items = new Dictionary<uint, (string Type, string? ContentType)>();
        if (children.FirstOrDefault(b => b.Type == "iinf") is { Type: not null } iinf)
        {
            var version = file[iinf.Start];
            var entries = Boxes(file, iinf.Start + 4 + (version == 0 ? 2 : 4), iinf.End);
            foreach (var infe in entries.Where(b => b.Type == "infe"))
            {
                var v = file[infe.Start];
                if (v < 2) continue;
                var p = infe.Start + 4;
                uint id = v == 2 ? BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(p)) : BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(p));
                p += (v == 2 ? 2 : 4) + 2; // item_ID, item_protection_index
                var itemType = Encoding.ASCII.GetString(file, p, 4);
                p += 4;
                ReadCString(file, ref p, infe.End); // item_name
                var contentType = itemType == "mime" ? ReadCString(file, ref p, infe.End) : null;
                items[id] = (itemType, contentType);
            }
        }

        var locations = children.FirstOrDefault(b => b.Type == "iloc") is { Type: not null } iloc
            ? ReadIloc(file, iloc)
            : [];
        var idat = children.FirstOrDefault(b => b.Type == "idat");

        byte[]? ItemData(uint id)
        {
            if (!locations.TryGetValue(id, out var extents)) return null;
            var data = new List<byte>();
            foreach (var (method, offset, length) in extents)
            {
                long start = method == 1 ? idat.Start + offset : offset;
                var end = method == 1 ? idat.End : file.Length;
                if (method > 1 || idat.Type is null && method == 1 || start < 0 || start + length > end) return null;
                data.AddRange(file.AsSpan((int)start, (int)length).ToArray());
            }
            return [.. data];
        }

        byte[]? exif = null, xmp = null;
        foreach (var (id, (type, contentType)) in items)
        {
            if (type == "Exif") exif ??= ItemData(id);
            else if (type == "mime" && contentType == "application/rdf+xml") xmp ??= ItemData(id);
        }

        byte[]? icc = null;
        if (children.FirstOrDefault(b => b.Type == "iprp") is { Type: not null } iprp &&
            Boxes(file, iprp.Start, iprp.End).FirstOrDefault(b => b.Type == "ipco") is { Type: not null } ipco)
        {
            foreach (var colr in Boxes(file, ipco.Start, ipco.End).Where(b => b.Type == "colr"))
            {
                var kind = Encoding.ASCII.GetString(file, colr.Start, 4);
                if (kind is "prof" or "rICC") { icc = file[(colr.Start + 4)..colr.End]; break; }
            }
        }
        return (exif, xmp, icc);
    }

    private readonly record struct Box(string Type, int Start, int End);

    /// <summary>The boxes between <paramref name="start"/> and <paramref name="end"/>; Start is where the content begins.</summary>
    private static IEnumerable<Box> Boxes(byte[] file, int start, int end)
    {
        for (var pos = start; pos + 8 <= end;)
        {
            long size = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(pos));
            var type = Encoding.ASCII.GetString(file, pos + 4, 4);
            var header = 8;
            if (size == 1)
            {
                if (pos + 16 > end) yield break;
                size = (long)BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan(pos + 8));
                header = 16;
            }
            else if (size == 0)
            {
                size = end - pos;
            }
            if (size < header || pos + size > end) yield break;
            yield return new Box(type, pos + header, (int)(pos + size));
            pos += (int)size;
        }
    }

    /// <summary>iloc: for each item, its extents as (construction method, offset, length).</summary>
    private static Dictionary<uint, List<(int Method, long Offset, long Length)>> ReadIloc(byte[] file, Box iloc)
    {
        var result = new Dictionary<uint, List<(int, long, long)>>();
        var version = file[iloc.Start];
        var p = iloc.Start + 4;
        var offsetSize = file[p] >> 4;
        var lengthSize = file[p] & 0xF;
        var baseOffsetSize = file[p + 1] >> 4;
        var indexSize = version is 1 or 2 ? file[p + 1] & 0xF : 0;
        p += 2;
        uint count;
        if (version < 2) { count = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(p)); p += 2; }
        else { count = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(p)); p += 4; }

        for (var i = 0; i < count && p < iloc.End; i++)
        {
            uint id;
            if (version < 2) { id = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(p)); p += 2; }
            else { id = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(p)); p += 4; }
            var method = 0;
            if (version is 1 or 2) { method = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(p)) & 0xF; p += 2; }
            p += 2; // data_reference_index
            var baseOffset = ReadSized(file, ref p, baseOffsetSize);
            int extentCount = BinaryPrimitives.ReadUInt16BigEndian(file.AsSpan(p));
            p += 2;
            var extents = new List<(int, long, long)>();
            for (var e = 0; e < extentCount; e++)
            {
                if (indexSize > 0) ReadSized(file, ref p, indexSize);
                var offset = ReadSized(file, ref p, offsetSize);
                var length = ReadSized(file, ref p, lengthSize);
                extents.Add((method, baseOffset + offset, length));
            }
            result[id] = extents;
        }
        return result;
    }

    private static long ReadSized(byte[] file, ref int p, int size)
    {
        long value = size switch
        {
            0 => 0,
            4 => BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(p)),
            8 => (long)BinaryPrimitives.ReadUInt64BigEndian(file.AsSpan(p)),
            _ => throw new InvalidDataException("Unexpected field size in iloc."),
        };
        p += size;
        return value;
    }

    private static string ReadCString(byte[] file, ref int p, int end)
    {
        var start = p;
        while (p < end && file[p] != 0) p++;
        var text = Encoding.UTF8.GetString(file, start, p - start);
        p++;
        return text;
    }
}
