using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;

namespace SmashIt.Core.Metadata;

/// <summary>Reads and rewrites the metadata chunks of a PNG, leaving the image data untouched.</summary>
internal static class PngMetadataIO
{
    private static readonly byte[] Signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private const string XmpKeyword = "XML:com.adobe.xmp";

    /// <summary>Chunks removed from the output before the selected metadata is written.</summary>
    private static readonly HashSet<string> MetadataChunks = ["eXIf", "tEXt", "zTXt", "iTXt", "tIME"];

    private sealed record Chunk(string Type, byte[] Data);

    public static ImageMetadata Read(byte[] file)
    {
        byte[]? exif = null, icc = null, xmp = null;
        var extras = new List<RawMetadataBlock>();

        foreach (var (type, data) in Parse(file))
        {
            switch (type)
            {
                case "eXIf":
                    exif ??= data;
                    break;
                case "iCCP":
                    icc ??= TryReadIcc(data);
                    break;
                case "iTXt" when IsXmp(data):
                    xmp ??= TryReadITxtText(data);
                    break;
                case "tEXt" or "zTXt" or "iTXt" or "tIME":
                    extras.Add(new RawMetadataBlock(type, data));
                    break;
            }
        }

        return new ImageMetadata
        {
            SourceFormat = Formats.MediaFormat.Png,
            Exif = exif,
            Icc = icc,
            Xmp = xmp,
            Extras = extras,
        };
    }

    /// <summary>
    /// Removes EXIF/text chunks from <paramref name="file"/> and inserts the given metadata.
    /// An existing iCCP chunk is kept: the encoder that wrote it knows it matches the pixel data.
    /// </summary>
    public static byte[] Write(byte[] file, ImageMetadata metadata)
    {
        var chunks = Parse(file).Where(c => !MetadataChunks.Contains(c.Type)).ToList();
        var hasIcc = chunks.Any(c => c.Type == "iCCP");

        var added = new List<Chunk>();
        if (metadata.Icc is { } icc && !hasIcc)
        {
            chunks.RemoveAll(c => c.Type == "sRGB"); // sRGB and iCCP must not coexist
            added.Add(new("iCCP", [.. "ICC Profile"u8, 0, 0, .. Deflate(icc)]));
        }
        if (metadata.Exif is { } exif) added.Add(new("eXIf", exif));
        if (metadata.Xmp is { } xmp)
            added.Add(new("iTXt", [.. Encoding.Latin1.GetBytes(XmpKeyword), 0, 0, 0, 0, 0, .. xmp]));
        foreach (var block in metadata.Extras)
            if (block.Type is "tEXt" or "zTXt" or "iTXt" or "tIME") added.Add(new(block.Type, block.Data));

        // Right after IHDR: before PLTE/IDAT as the spec requires for iCCP and eXIf.
        chunks.InsertRange(1, added);
        return Serialize(chunks, file.Length + added.Sum(c => c.Data.Length + 12));
    }

    /// <summary>True when the PNG contains a chunk of this type (e.g. <c>acTL</c> for an animated PNG).</summary>
    public static bool HasChunk(byte[] file, string type) => Parse(file).Any(c => c.Type == type);

    /// <summary>Returns the PNG without the chunks of the given types; the other chunks are copied as they are.</summary>
    public static byte[] RemoveChunks(byte[] file, IReadOnlySet<string> types)
    {
        var chunks = Parse(file).Where(c => !types.Contains(c.Type)).ToList();
        return Serialize(chunks, file.Length);
    }

    private static byte[] Serialize(List<Chunk> chunks, int capacity)
    {
        using var output = new MemoryStream(capacity);
        output.Write(Signature);
        Span<byte> header = stackalloc byte[8];
        Span<byte> crcBytes = stackalloc byte[4];
        foreach (var (type, data) in chunks)
        {
            BinaryPrimitives.WriteUInt32BigEndian(header, (uint)data.Length);
            Encoding.ASCII.GetBytes(type, header[4..]);
            output.Write(header);
            output.Write(data);
            var crc = Crc32.Compute(header[4..], data);
            BinaryPrimitives.WriteUInt32BigEndian(crcBytes, crc);
            output.Write(crcBytes);
        }
        return output.ToArray();
    }

    private static List<Chunk> Parse(byte[] file)
    {
        if (!file.AsSpan().StartsWith(Signature)) throw new InvalidDataException("Fichier PNG invalide.");

        var chunks = new List<Chunk>();
        var pos = Signature.Length;
        while (pos + 12 <= file.Length)
        {
            var length = BinaryPrimitives.ReadUInt32BigEndian(file.AsSpan(pos));
            if (length > int.MaxValue || pos + 12 + (long)length > file.Length)
                throw new InvalidDataException(L10n.T("Truncated PNG chunk.", "Bloc PNG tronqué."));
            var type = Encoding.ASCII.GetString(file, pos + 4, 4);
            chunks.Add(new(type, file.AsSpan(pos + 8, (int)length).ToArray()));
            pos += 12 + (int)length;
            if (type == "IEND") break;
        }

        if (chunks.Count < 2 || chunks[0].Type != "IHDR" || chunks[^1].Type != "IEND")
            throw new InvalidDataException("Structure PNG invalide.");
        return chunks;
    }

    private static byte[]? TryReadIcc(byte[] data)
    {
        var nameEnd = Array.IndexOf(data, (byte)0);
        if (nameEnd < 1 || nameEnd + 2 > data.Length || data[nameEnd + 1] != 0) return null;
        try
        {
            return Inflate(data.AsSpan(nameEnd + 2));
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static bool IsXmp(byte[] data)
    {
        var keyword = Encoding.Latin1.GetBytes(XmpKeyword);
        return data.Length > keyword.Length && data.AsSpan().StartsWith(keyword) && data[keyword.Length] == 0;
    }

    /// <summary>iTXt: keyword\0 flag method lang\0 translated\0 text.</summary>
    private static byte[]? TryReadITxtText(byte[] data)
    {
        var pos = Array.IndexOf(data, (byte)0) + 1;
        if (pos <= 0 || pos + 2 > data.Length) return null;
        var compressed = data[pos] == 1;
        pos += 2;
        for (var i = 0; i < 2; i++)
        {
            var end = Array.IndexOf(data, (byte)0, pos);
            if (end < 0) return null;
            pos = end + 1;
        }
        var text = data.AsSpan(pos);
        try
        {
            return compressed ? Inflate(text) : text.ToArray();
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }

    private static byte[] Inflate(ReadOnlySpan<byte> zlib)
    {
        using var input = new MemoryStream(zlib.ToArray());
        using var stream = new ZLibStream(input, CompressionMode.Decompress);
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var stream = new ZLibStream(output, CompressionLevel.SmallestSize, leaveOpen: true))
            stream.Write(data);
        return output.ToArray();
    }

    private static class Crc32
    {
        private static readonly uint[] Table = BuildTable();

        public static uint Compute(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in type) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            foreach (var b in data) crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return crc ^ 0xFFFFFFFFu;
        }

        private static uint[] BuildTable()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                var c = n;
                for (var k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[n] = c;
            }
            return table;
        }
    }
}
