using System.IO.Compression;
using System.Text;
using SmashIt.Core.Formats;

namespace SmashIt.Core.Tests;

/// <summary>Writes small but real PDFs: one page, a JPEG image, a Flate image, some text, a link.</summary>
internal static class TestPdf
{
    public static async Task<string> WriteAsync(string path, bool signed = false)
    {
        // A high-quality baseline JPEG: room for jpegtran and for lossy re-encoding.
        var jpegPath = path + ".img.jpg";
        await TestImages.WriteAsync(jpegPath, MediaFormat.Jpeg, 400, 300);
        var jpeg = await File.ReadAllBytesAsync(jpegPath);
        File.Delete(jpegPath);

        // A smooth RGB gradient stored with Flate: lossless, but JPEG would be much smaller.
        const int w = 300, h = 200;
        var raw = new byte[w * h * 3];
        for (var y = 0; y < h; y++)
        for (var x = 0; x < w; x++)
        {
            var i = (y * w + x) * 3;
            raw[i] = (byte)(x * 255 / w);
            raw[i + 1] = (byte)(y * 255 / h);
            raw[i + 2] = (byte)((x + y) % 256);
        }
        var flate = Deflate(raw);

        var content = Encoding.ASCII.GetBytes(
            "BT /F1 24 Tf 50 750 Td (Smash It) Tj ET\n" +
            "q 300 0 0 225 50 450 cm /Im1 Do Q\nq 300 0 0 200 50 200 cm /Im2 Do Q\n");

        var objects = new List<byte[]>
        {
            Obj(1, "<< /Type /Catalog /Pages 2 0 R" + (signed ? " /AcroForm << /Fields [8 0 R] /SigFlags 3 >>" : "") + " >>"),
            Obj(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
            Obj(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] " +
                   "/Resources << /XObject << /Im1 4 0 R /Im2 5 0 R >> /Font << /F1 7 0 R >> >> " +
                   "/Annots [<< /Type /Annot /Subtype /Link /Rect [50 740 200 780] /A << /S /URI /URI (https://example.org) >> >>] " +
                   "/Contents 6 0 R >>"),
            Stream(4, $"/Type /XObject /Subtype /Image /Width 400 /Height 300 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /DCTDecode", jpeg),
            Stream(5, $"/Type /XObject /Subtype /Image /Width {w} /Height {h} /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode", flate),
            Stream(6, "", content),
            Obj(7, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"),
        };
        if (signed)
        {
            objects.Add(Obj(8, "<< /FT /Sig /T (Signature) /V 9 0 R /Rect [0 0 0 0] >>"));
            objects.Add(Obj(9, "<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached " +
                               "/ByteRange [0 0 0 0] /Contents <00> >>"));
        }

        await using var file = File.Create(path);
        var offsets = new List<long>();
        file.Write("%PDF-1.7\n%\xE2\xE3\xCF\xD3\n"u8);
        foreach (var obj in objects)
        {
            offsets.Add(file.Position);
            file.Write(obj);
        }
        var xref = file.Position;
        var trailer = new StringBuilder();
        trailer.Append($"xref\n0 {objects.Count + 1}\n0000000000 65535 f \n");
        foreach (var offset in offsets) trailer.Append($"{offset:D10} 00000 n \n");
        trailer.Append($"trailer\n<< /Size {objects.Count + 1} /Root 1 0 R /Info << /Title (Test) >> >>\nstartxref\n{xref}\n%%EOF\n");
        file.Write(Encoding.ASCII.GetBytes(trailer.ToString()));
        return path;
    }

    private static byte[] Obj(int number, string body) =>
        Encoding.ASCII.GetBytes($"{number} 0 obj\n{body}\nendobj\n");

    private static byte[] Stream(int number, string dict, byte[] data)
    {
        var head = Encoding.ASCII.GetBytes($"{number} 0 obj\n<< {dict} /Length {data.Length} >>\nstream\n");
        var tail = "\nendstream\nendobj\n"u8;
        return [.. head, .. data, .. tail];
    }

    private static byte[] Deflate(byte[] data)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, CompressionLevel.Fastest, leaveOpen: true)) z.Write(data);
        return output.ToArray();
    }
}
