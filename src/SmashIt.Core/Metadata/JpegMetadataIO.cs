namespace SmashIt.Core.Metadata;

/// <summary>Reads and rewrites the metadata segments (APPn, COM) of a JPEG, leaving the image data untouched.</summary>
internal static class JpegMetadataIO
{
    private const byte App0 = 0xE0, App1 = 0xE1, App2 = 0xE2, App13 = 0xED, App14 = 0xEE, Com = 0xFE;
    private const int MaxPayload = 65533;

    private static readonly byte[] ExifHeader = "Exif\0\0"u8.ToArray();
    private static readonly byte[] XmpHeader = "http://ns.adobe.com/xap/1.0/\0"u8.ToArray();
    private static readonly byte[] IccHeader = "ICC_PROFILE\0"u8.ToArray();

    private sealed record Segment(byte Marker, byte[]? Payload);

    public static ImageMetadata Read(byte[] file)
    {
        var (segments, _) = Parse(file);
        byte[]? exif = null, xmp = null;
        var iccChunks = new SortedDictionary<int, byte[]>();
        var extras = new List<RawMetadataBlock>();

        foreach (var (marker, payload) in segments)
        {
            if (payload is null) continue;
            switch (marker)
            {
                case App1 when payload.AsSpan().StartsWith(ExifHeader):
                    exif ??= payload[ExifHeader.Length..];
                    break;
                case App1 when payload.AsSpan().StartsWith(XmpHeader):
                    xmp ??= payload[XmpHeader.Length..];
                    break;
                case App2 when payload.AsSpan().StartsWith(IccHeader) && payload.Length > IccHeader.Length + 2:
                    iccChunks[payload[IccHeader.Length]] = payload[(IccHeader.Length + 2)..];
                    break;
                case App0 or App14:
                    break; // part of the image encoding, not metadata
                default:
                    if (IsMetadataMarker(marker)) extras.Add(new RawMetadataBlock($"{marker:X2}", payload));
                    break;
            }
        }

        return new ImageMetadata
        {
            SourceFormat = Formats.MediaFormat.Jpeg,
            Exif = exif,
            Xmp = xmp,
            Icc = iccChunks.Count > 0 ? iccChunks.Values.SelectMany(c => c).ToArray() : null,
            Extras = extras,
        };
    }

    /// <summary>Removes every metadata segment from <paramref name="file"/> and inserts the given ones.</summary>
    public static byte[] Write(byte[] file, ImageMetadata metadata)
    {
        var (segments, bodyOffset) = Parse(file);
        var kept = segments.Where(s => !IsMetadataMarker(s.Marker)).ToList();

        var added = new List<Segment>();
        if (metadata.Exif is { } exif && ExifHeader.Length + exif.Length <= MaxPayload)
            added.Add(new(App1, [.. ExifHeader, .. exif]));
        if (metadata.Xmp is { } xmp && XmpHeader.Length + xmp.Length <= MaxPayload)
            added.Add(new(App1, [.. XmpHeader, .. xmp]));
        if (metadata.Icc is { } icc)
        {
            const int chunkSize = MaxPayload - 14;
            var count = (icc.Length + chunkSize - 1) / chunkSize;
            if (count is > 0 and <= 255)
            {
                for (var i = 0; i < count; i++)
                {
                    var data = icc.AsSpan(i * chunkSize, Math.Min(chunkSize, icc.Length - i * chunkSize));
                    added.Add(new(App2, [.. IccHeader, (byte)(i + 1), (byte)count, .. data]));
                }
            }
        }
        foreach (var block in metadata.Extras)
        {
            if (block.Type.Length == 2 && byte.TryParse(block.Type, System.Globalization.NumberStyles.HexNumber, null, out var marker)
                && IsMetadataMarker(marker) && block.Data.Length <= MaxPayload)
                added.Add(new(marker, block.Data));
        }

        // JFIF (APP0) must stay first; metadata goes right after it.
        var insertAt = kept.Count > 0 && kept[0].Marker == App0 ? 1 : 0;
        kept.InsertRange(insertAt, added);

        using var output = new MemoryStream(file.Length + added.Sum(s => s.Payload!.Length + 4));
        output.Write([0xFF, 0xD8]);
        foreach (var (marker, payload) in kept)
        {
            output.WriteByte(0xFF);
            output.WriteByte(marker);
            if (payload is null) continue;
            var length = payload.Length + 2;
            output.WriteByte((byte)(length >> 8));
            output.WriteByte((byte)length);
            output.Write(payload);
        }
        output.Write(file.AsSpan(bodyOffset));
        return output.ToArray();
    }

    /// <summary>APP1–APP13, APP15 and COM. APP0 (JFIF) and APP14 (Adobe color transform) are kept.</summary>
    private static bool IsMetadataMarker(byte marker) =>
        marker is >= App1 and <= App13 or 0xEF or Com;

    /// <summary>Segments before the first SOS, and the offset where the image data starts.</summary>
    private static (List<Segment> Segments, int BodyOffset) Parse(byte[] data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
            throw new InvalidDataException("Fichier JPEG invalide.");

        var segments = new List<Segment>();
        var pos = 2;
        while (pos + 1 < data.Length)
        {
            if (data[pos] != 0xFF) throw new InvalidDataException("Structure JPEG invalide.");
            var marker = data[pos + 1];
            if (marker == 0xFF) { pos++; continue; }                 // fill byte
            if (marker is 0xDA or 0xD9) return (segments, pos);      // SOS / EOI: the rest is copied verbatim
            if (marker is >= 0xD0 and <= 0xD7 or 0x01)
            {
                segments.Add(new(marker, null));
                pos += 2;
                continue;
            }
            if (pos + 4 > data.Length) break;
            var length = (data[pos + 2] << 8) | data[pos + 3];
            if (length < 2 || pos + 2 + length > data.Length) throw new InvalidDataException("Segment JPEG tronqué.");
            segments.Add(new(marker, data.AsSpan(pos + 4, length - 2).ToArray()));
            pos += 2 + length;
        }
        throw new InvalidDataException("Fichier JPEG tronqué.");
    }
}
