#:property TargetFramework=net10.0-windows10.0.19041.0
#:package System.Drawing.Common@9.0.0
// Builds Smash It's images from the artwork in assets/branding:
//   - the Windows icon (.ico, 16–256 px), from the mascot head: window, taskbar, Alt+Tab, Explorer;
//   - the title bar icon, from the mascot head;
//   - the splash screen, from the full mascot;
//   - the README mascot and an icon preview sheet.
// The PNGs shipped with the app go through oxipng when tools/bin/oxipng.exe is there (run tools/fetch-tools.ps1 first).
//
// usage (from the repo root):
//   dotnet run tools/branding/make-icon.cs -- assets/branding/mascot.png assets/branding/mascot-head.png src/SmashIt.App/Assets assets/branding
#pragma warning disable CA1416 // Windows only
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var (mascotPath, headPath, outDir, brandingDir) = (args[0], args[1], args[2], args[3]);
Directory.CreateDirectory(outDir);
Directory.CreateDirectory(brandingDir);

int[] sizes = [16, 20, 24, 32, 40, 48, 64, 96, 128, 256];

using var headArt = new Bitmap(headPath);
var entries = new List<(int Size, byte[] Png)>();
foreach (var size in sizes)
{
    using var scaled = Downscale(headArt, size);
    entries.Add((size, Png(scaled)));
}
WriteIco(Path.Combine(outDir, "SmashIt.ico"), entries);

// Title bar icon: shown at 18 px; 72 px keeps it sharp up to 400 % scale.
var titleBarPath = Path.Combine(outDir, "SmashIt-titlebar.png");
using (var titleBar = Downscale(headArt, 72))
    titleBar.Save(titleBarPath, ImageFormat.Png);

// Splash screen: the full mascot, 300 px tall on screen, so 600 px for 200 % scale.
var splashPath = Path.Combine(outDir, "SmashIt-splash.png");
using (var mascot = new Bitmap(mascotPath))
using (var splash = Downscale(mascot, (int)Math.Round(600.0 * mascot.Width / mascot.Height), (double)mascot.Height / mascot.Width))
    splash.Save(splashPath, ImageFormat.Png);

Optimize(titleBarPath, splashPath);

// README picture: the mascot at a web-friendly size.
using (var mascot = new Bitmap(mascotPath))
using (var small = Downscale(mascot, 360, (double)mascot.Height / mascot.Width))
    small.Save(Path.Combine(brandingDir, "mascot-360.png"), ImageFormat.Png);

SavePreview(Path.Combine(brandingDir, "icon-preview.png"), entries);
Console.WriteLine($"Wrote {Path.Combine(outDir, "SmashIt.ico")} ({entries.Count} sizes)");

// ---------------------------------------------------------------------------------------------

/// <summary>Losslessly shrinks the PNGs shipped with the app, when oxipng is available.</summary>
static void Optimize(params string[] paths)
{
    var oxipng = Path.Combine("tools", "bin", "oxipng.exe");
    if (!File.Exists(oxipng))
    {
        Console.WriteLine("oxipng not found: PNGs left unoptimized (run tools/fetch-tools.ps1).");
        return;
    }
    var psi = new ProcessStartInfo(oxipng) { UseShellExecute = false };
    foreach (var arg in (string[])["-o", "max", "--strip", "safe", "--alpha", .. paths]) psi.ArgumentList.Add(arg);
    using var process = Process.Start(psi)!;
    process.WaitForExit();
}


/// <summary>High-quality downscale by successive halving, then a final bicubic step.</summary>
static Bitmap Downscale(Bitmap source, int width, double aspect = 1)
{
    var height = (int)Math.Round(width * aspect);
    Bitmap current = new(source);
    while (current.Width / 2 >= width)
    {
        var half = Resize(current, current.Width / 2, current.Height / 2);
        current.Dispose();
        current = half;
    }
    var result = Resize(current, width, height);
    current.Dispose();
    return result;
}

static Bitmap Resize(Bitmap source, int width, int height)
{
    var bmp = new Bitmap(width, height, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.CompositingQuality = CompositingQuality.HighQuality;
    using var attributes = new ImageAttributes();
    attributes.SetWrapMode(WrapMode.TileFlipXY); // no dark fringe at the borders
    g.DrawImage(source, new Rectangle(0, 0, width, height), 0, 0, source.Width, source.Height, GraphicsUnit.Pixel, attributes);
    return bmp;
}

static byte[] Png(Bitmap bmp)
{
    using var ms = new MemoryStream();
    bmp.Save(ms, ImageFormat.Png);
    return ms.ToArray();
}

/// <summary>ICO with PNG-compressed entries (supported since Windows Vista).</summary>
static void WriteIco(string path, List<(int Size, byte[] Png)> entries)
{
    using var file = File.Create(path);
    using var w = new BinaryWriter(file);
    w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)entries.Count);
    var offset = 6 + 16 * entries.Count;
    foreach (var (size, png) in entries)
    {
        w.Write((byte)(size >= 256 ? 0 : size)); w.Write((byte)(size >= 256 ? 0 : size));
        w.Write((byte)0); w.Write((byte)0);
        w.Write((ushort)1); w.Write((ushort)32);
        w.Write(png.Length); w.Write(offset);
        offset += png.Length;
    }
    foreach (var (_, png) in entries) w.Write(png);
}

/// <summary>Every size at 1:1 and zoomed ×4, on a dark and a light background.</summary>
static void SavePreview(string path, List<(int Size, byte[] Png)> entries)
{
    const int zoom = 4, pad = 16;
    var row = entries.Sum(e => e.Size + pad) + pad;
    var height = pad * 3 + 256 + 40 * zoom;
    using var preview = new Bitmap(row + entries.Where(e => e.Size <= 40).Sum(e => e.Size * zoom + pad), height * 2);
    using var g = Graphics.FromImage(preview);
    g.InterpolationMode = InterpolationMode.NearestNeighbor;
    g.PixelOffsetMode = PixelOffsetMode.Half;
    Color[] backs = [Color.FromArgb(0x20, 0x20, 0x20), Color.FromArgb(0xF3, 0xF3, 0xF3)];
    for (var b = 0; b < 2; b++)
    {
        var top = b * height;
        using (var back = new SolidBrush(backs[b])) g.FillRectangle(back, 0, top, preview.Width, height);
        var x = pad;
        foreach (var (size, png) in entries)
        {
            using var img = new Bitmap(new MemoryStream(png));
            g.DrawImage(img, x, top + pad, size, size);
            x += size + pad;
        }
        foreach (var (size, png) in entries.Where(e => e.Size <= 40))
        {
            using var img = new Bitmap(new MemoryStream(png));
            g.DrawImage(img, x, top + pad, size * zoom, size * zoom);
            x += size * zoom + pad;
        }
    }
    preview.Save(path, ImageFormat.Png);
}
