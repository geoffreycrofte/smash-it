using System.Text;
using SmashIt.Core.Formats;

namespace SmashIt.Core.Tests;

public class FormatDetectorTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0, 0x10 }, MediaFormat.Jpeg)]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0 }, MediaFormat.Png)]
    [InlineData(new byte[] { 0x49, 0x49, 0x2A, 0x00, 8, 0 }, MediaFormat.Tiff)]
    [InlineData(new byte[] { 0x4D, 0x4D, 0x00, 0x2A, 0, 8 }, MediaFormat.Tiff)]
    [InlineData(new byte[] { 0x49, 0x49, 0xBC, 0x01 }, MediaFormat.JpegXr)]
    [InlineData(new byte[] { 0x1A, 0x45, 0xDF, 0xA3, 0x9F }, MediaFormat.Mkv)]
    [InlineData(new byte[] { 0x00, 0x01, 0x02 }, MediaFormat.Unknown)]
    [InlineData(new byte[0], MediaFormat.Unknown)]
    public void Detects_binary_signatures(byte[] header, MediaFormat expected) =>
        Assert.Equal(expected, FormatDetector.Detect(header));

    [Theory]
    [InlineData("GIF89a....", MediaFormat.Gif)]
    [InlineData("%PDF-1.7\n", MediaFormat.Pdf)]
    [InlineData("RIFF\0\0\0\0WEBPVP8 ", MediaFormat.WebP)]
    [InlineData("RIFF\0\0\0\0AVI LIST", MediaFormat.Avi)]
    [InlineData("BM\0\0\0\0\0\0\0\0\0\0\0\0\0\0", MediaFormat.Bmp)]
    [InlineData("<?xml version=\"1.0\"?>\n<svg xmlns=\"http://www.w3.org/2000/svg\"/>", MediaFormat.Svg)]
    [InlineData("  <svg viewBox=\"0 0 1 1\"></svg>", MediaFormat.Svg)]
    [InlineData("<?xml version=\"1.0\"?><html/>", MediaFormat.Unknown)]
    public void Detects_ascii_signatures(string header, MediaFormat expected) =>
        Assert.Equal(expected, FormatDetector.Detect(Encoding.Latin1.GetBytes(header)));

    [Theory]
    [InlineData("heic", new[] { "mif1", "heic" }, MediaFormat.Heif)]
    [InlineData("mif1", new[] { "mif1", "miaf" }, MediaFormat.Heif)]
    [InlineData("avif", new[] { "avif", "mif1", "miaf" }, MediaFormat.Avif)]
    [InlineData("mif1", new[] { "avif", "mif1" }, MediaFormat.Avif)]
    [InlineData("isom", new[] { "isom", "iso2", "avc1", "mp41" }, MediaFormat.Mp4)]
    [InlineData("qt  ", new[] { "qt  " }, MediaFormat.Mov)]
    public void Tells_iso_bmff_files_apart_by_brand(string major, string[] compatible, MediaFormat expected) =>
        Assert.Equal(expected, FormatDetector.Detect(Ftyp(major, compatible)));

    private static byte[] Ftyp(string major, string[] compatible)
    {
        var size = 16 + compatible.Length * 4;
        var bytes = new List<byte> { 0, 0, 0, (byte)size };
        bytes.AddRange(Encoding.ASCII.GetBytes("ftyp" + major));
        bytes.AddRange([0, 0, 0, 0]);
        foreach (var brand in compatible) bytes.AddRange(Encoding.ASCII.GetBytes(brand));
        bytes.AddRange(new byte[32]);
        return [.. bytes];
    }
}
