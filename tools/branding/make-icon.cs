#:property TargetFramework=net10.0-windows10.0.19041.0
#:package System.Drawing.Common@9.0.0
// Builds Smash It's Windows icon (.ico) and its previews.
//
// Small sizes (16–40 px) use a vector bear head drawn here: the detailed artwork turns to mush below 48 px.
// Large sizes (48–256 px) use the detailed artwork (assets/branding/smash-it-icon.png).
//
// It also writes the title bar icon (from the mascot head) and the splash screen (the full mascot),
// both run through oxipng when tools/bin/oxipng.exe is there (run tools/fetch-tools.ps1 first).
//
// usage (from the repo root):
//   dotnet run tools/branding/make-icon.cs -- assets/branding/smash-it-icon.png assets/branding/mascot.png assets/branding/mascot-head.png src/SmashIt.App/Assets assets/branding
#pragma warning disable CA1416 // Windows only
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

var (artworkPath, mascotPath, headPath, outDir, brandingDir) = (args[0], args[1], args[2], args[3], args[4]);
Directory.CreateDirectory(outDir);
Directory.CreateDirectory(brandingDir);

int[] smallSizes = [16, 20, 24, 32, 40];
int[] largeSizes = [48, 64, 96, 128, 256];

using var artwork = new Bitmap(artworkPath);
var entries = new List<(int Size, byte[] Png)>();
foreach (var size in smallSizes)
{
    using var head = DrawHead(size);
    entries.Add((size, Png(head)));
}
foreach (var size in largeSizes)
{
    using var scaled = Downscale(artwork, size);
    entries.Add((size, Png(scaled)));
}
// Title bar icon: the mascot head, shown at 18 px; 72 px keeps it sharp up to 400 % scale.
var titleBarPath = Path.Combine(outDir, "SmashIt-titlebar.png");
using (var headArt = new Bitmap(headPath))
using (var titleBar = Downscale(headArt, 72))
    titleBar.Save(titleBarPath, ImageFormat.Png);

// Splash screen: the full mascot, 300 px tall on screen, so 600 px for 200 % scale.
var splashPath = Path.Combine(outDir, "SmashIt-splash.png");
using (var mascot = new Bitmap(mascotPath))
using (var splash = Downscale(mascot, (int)Math.Round(600.0 * mascot.Width / mascot.Height), (double)mascot.Height / mascot.Width))
    splash.Save(splashPath, ImageFormat.Png);

Optimize(titleBarPath, splashPath);
using (var headLarge = DrawHead(256)) headLarge.Save(Path.Combine(brandingDir, "smash-it-head.png"), ImageFormat.Png);
WriteIco(Path.Combine(outDir, "SmashIt.ico"), entries);

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

/// <summary>The bear head, designed on a 256 grid and drawn natively at <paramref name="size"/> pixels.</summary>
static Bitmap DrawHead(int size)
{
    var fur = Color.FromArgb(0x23, 0x1A, 0x1D);
    var red = Color.FromArgb(0xE3, 0x16, 0x1E);
    var tan = Color.FromArgb(0xD9, 0xB2, 0x8E);
    var white = Color.FromArgb(0xF4, 0xEE, 0xE6);

    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.Clear(Color.Transparent);
    var k = size / 256f;
    g.ScaleTransform(k, k);

    // Silhouette: two ears + head. Stroked in red first: a rim that keeps the black head visible
    // on a dark taskbar (at least ~1 px whatever the size).
    using var silhouette = new GraphicsPath();
    silhouette.AddEllipse(Circle(60, 72, 44));
    silhouette.AddEllipse(Circle(196, 72, 44));
    silhouette.AddEllipse(28, 52, 200, 192);
    var rim = Math.Max(1.6f / k, 10f);
    using (var rimPen = new Pen(red, rim) { LineJoin = LineJoin.Round })
        g.DrawPath(rimPen, silhouette);
    using (var furBrush = new SolidBrush(fur))
    {
        g.FillEllipse(furBrush, Circle(60, 72, 44));
        g.FillEllipse(furBrush, Circle(196, 72, 44));
        g.FillEllipse(furBrush, 28, 52, 200, 192);
    }

    using var redBrush = new SolidBrush(red);
    using var furFill = new SolidBrush(fur);
    // Inner ears.
    g.FillEllipse(redBrush, Circle(64, 76, 22));
    g.FillEllipse(redBrush, Circle(192, 76, 22));

    // Spiky red tuft swept up and to the right, like the mascot's.
    g.FillPolygon(redBrush, new PointF[]
    {
        new(100, 90), new(106, 42), new(124, 66), new(140, 6), new(152, 62), new(196, 26), new(168, 86), new(134, 98),
    });

    // Eyes: white, cut by angry brows sloping down towards the nose.
    using (var whiteBrush = new SolidBrush(white))
    {
        g.FillEllipse(whiteBrush, 62, 124, 54, 38);
        g.FillEllipse(whiteBrush, 140, 124, 54, 38);
    }
    g.FillPolygon(furFill, new PointF[] { new(56, 104), new(126, 104), new(126, 150), new(56, 126) });
    g.FillPolygon(furFill, new PointF[] { new(200, 104), new(130, 104), new(130, 150), new(200, 126) });
    g.FillEllipse(furFill, Circle(98, 148, 10));
    g.FillEllipse(furFill, Circle(158, 148, 10));

    // Muzzle and nose.
    using (var tanBrush = new SolidBrush(tan))
        g.FillEllipse(tanBrush, 82, 162, 92, 66);
    g.FillEllipse(furFill, 112, 168, 32, 22);
    if (size >= 32)
    {
        using var mouth = new Pen(fur, 5f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
        g.DrawBezier(mouth, new PointF(112, 206), new PointF(124, 212), new PointF(140, 210), new PointF(150, 200));
    }
    return bmp;
}

static RectangleF Circle(float cx, float cy, float r) => new(cx - r, cy - r, r * 2, r * 2);

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
