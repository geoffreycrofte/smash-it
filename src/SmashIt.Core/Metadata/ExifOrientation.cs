using System.Buffers.Binary;

namespace SmashIt.Core.Metadata;

/// <summary>Reads and writes the Orientation tag (0x0112) in IFD0 of an EXIF TIFF structure.</summary>
public static class ExifOrientation
{
    private const ushort OrientationTag = 0x0112;
    private const ushort TypeShort = 3;

    /// <summary>1–8, or <c>null</c> when the tag is absent or invalid.</summary>
    public static int? Read(ReadOnlySpan<byte> tiff)
    {
        var offset = FindValueOffset(tiff, out var littleEndian);
        if (offset < 0) return null;
        var value = ReadUInt16(tiff[offset..], littleEndian);
        return value is >= 1 and <= 8 ? value : null;
    }

    /// <summary>Returns a copy of <paramref name="tiff"/> with the orientation set (unchanged if the tag is absent).</summary>
    public static byte[] WithOrientation(byte[] tiff, int orientation)
    {
        var copy = (byte[])tiff.Clone();
        var offset = FindValueOffset(copy, out var littleEndian);
        if (offset >= 0)
        {
            if (littleEndian) BinaryPrimitives.WriteUInt16LittleEndian(copy.AsSpan(offset), (ushort)orientation);
            else BinaryPrimitives.WriteUInt16BigEndian(copy.AsSpan(offset), (ushort)orientation);
        }
        return copy;
    }

    /// <summary>A minimal EXIF block holding only the orientation (26 bytes).</summary>
    public static byte[] CreateMinimal(int orientation)
    {
        var tiff = new byte[26];
        "II"u8.CopyTo(tiff);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(2), 42);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(4), 8);       // IFD0 offset
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 1);       // 1 entry
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(10), OrientationTag);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(12), TypeShort);
        BinaryPrimitives.WriteUInt32LittleEndian(tiff.AsSpan(14), 1);      // count
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(18), (ushort)orientation);
        // bytes 20-21: value padding, 22-25: next IFD offset = 0
        return tiff;
    }

    private static int FindValueOffset(ReadOnlySpan<byte> tiff, out bool littleEndian)
    {
        littleEndian = false;
        if (tiff.Length < 8) return -1;
        if (tiff[0] == 'I' && tiff[1] == 'I') littleEndian = true;
        else if (tiff[0] != 'M' || tiff[1] != 'M') return -1;

        var ifd = littleEndian ? BinaryPrimitives.ReadUInt32LittleEndian(tiff[4..]) : BinaryPrimitives.ReadUInt32BigEndian(tiff[4..]);
        if (ifd > int.MaxValue - 2 || ifd + 2 > tiff.Length) return -1;

        var count = ReadUInt16(tiff[(int)ifd..], littleEndian);
        for (var i = 0; i < count; i++)
        {
            var entry = (int)ifd + 2 + i * 12;
            if (entry + 12 > tiff.Length) return -1;
            if (ReadUInt16(tiff[entry..], littleEndian) == OrientationTag &&
                ReadUInt16(tiff[(entry + 2)..], littleEndian) == TypeShort)
                return entry + 8;
        }
        return -1;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> span, bool littleEndian) =>
        littleEndian ? BinaryPrimitives.ReadUInt16LittleEndian(span) : BinaryPrimitives.ReadUInt16BigEndian(span);
}
