using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SmashIt.App.Services;

/// <summary>
/// Startup splash: the app icon floating on a transparent background, shown before WinUI starts.
/// A plain Win32 layered window (per-pixel alpha), so it appears while the XAML framework is still loading.
/// </summary>
internal sealed partial class SplashScreen : IDisposable
{
    private const int HeightDips = 300;

    private readonly IntPtr _hwnd;
    private readonly IntPtr _memDc;
    private readonly IntPtr _bitmap;
    private readonly IntPtr _oldBitmap;
    private readonly POINT _position;
    private readonly SIZE _size;
    private readonly long _shownAt = Environment.TickCount64;

    private SplashScreen(IntPtr hwnd, IntPtr memDc, IntPtr bitmap, IntPtr oldBitmap, POINT position, SIZE size)
    {
        (_hwnd, _memDc, _bitmap, _oldBitmap, _position, _size) = (hwnd, memDc, bitmap, oldBitmap, position, size);
    }

    /// <summary>The time the splash has been on screen.</summary>
    public TimeSpan Elapsed => TimeSpan.FromMilliseconds(Environment.TickCount64 - _shownAt);

    /// <summary>Shows the splash centered on the screen under the mouse; null if anything goes wrong (no splash, no crash).</summary>
    public static SplashScreen? TryShow()
    {
        try
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Assets", "SmashIt-splash.png");
            if (!File.Exists(path)) return null;

            GetCursorPos(out var cursor);
            var monitor = MonitorFromPoint(cursor, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            if (!GetMonitorInfoW(monitor, ref info)) return null;
            GetDpiForMonitor(monitor, 0, out var dpi, out _);
            var height = (int)Math.Round(HeightDips * (dpi == 0 ? 96u : dpi) / 96.0);

            var (pixels, width) = Decode(path, height);
            var work = info.rcWork;
            var position = new POINT
            {
                X = work.Left + (work.Right - work.Left - width) / 2,
                Y = work.Top + (work.Bottom - work.Top - height) / 2,
            };
            var size = new SIZE { cx = width, cy = height };

            var screenDc = GetDC(IntPtr.Zero);
            var memDc = CreateCompatibleDC(screenDc);
            var header = new BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<BITMAPINFOHEADER>(),
                biWidth = width,
                biHeight = -height, // top-down
                biPlanes = 1,
                biBitCount = 32,
            };
            var bitmap = CreateDIBSection(screenDc, ref header, 0, out var bits, IntPtr.Zero, 0);
            ReleaseDC(IntPtr.Zero, screenDc);
            if (bitmap == IntPtr.Zero)
            {
                DeleteDC(memDc);
                return null;
            }
            Marshal.Copy(pixels, 0, bits, pixels.Length);
            var oldBitmap = SelectObject(memDc, bitmap);

            // The "Static" system class: nothing to register, and a layered window paints itself from the bitmap.
            var hwnd = CreateWindowExW(
                WS_EX_LAYERED | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE,
                "Static", "Smash It", WS_POPUP,
                position.X, position.Y, width, height,
                IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
            if (hwnd == IntPtr.Zero)
            {
                SelectObject(memDc, oldBitmap);
                DeleteObject(bitmap);
                DeleteDC(memDc);
                return null;
            }

            var splash = new SplashScreen(hwnd, memDc, bitmap, oldBitmap, position, size);
            splash.SetOpacity(255);
            ShowWindow(hwnd, SW_SHOWNOACTIVATE);
            return splash;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Fades the splash out, then closes it.</summary>
    public async Task FadeOutAsync()
    {
        const int steps = 12;
        for (var i = steps - 1; i >= 0; i--)
        {
            SetOpacity((byte)(255 * i / steps));
            await Task.Delay(16);
        }
        Dispose();
    }

    private void SetOpacity(byte alpha)
    {
        var blend = new BLENDFUNCTION { BlendOp = AC_SRC_OVER, SourceConstantAlpha = alpha, AlphaFormat = AC_SRC_ALPHA };
        var position = _position;
        var size = _size;
        var source = new POINT();
        UpdateLayeredWindow(_hwnd, IntPtr.Zero, ref position, ref size, _memDc, ref source, 0, ref blend, ULW_ALPHA);
    }

    public void Dispose()
    {
        DestroyWindow(_hwnd);
        SelectObject(_memDc, _oldBitmap);
        DeleteObject(_bitmap);
        DeleteDC(_memDc);
    }

    /// <summary>
    /// Decodes the PNG to premultiplied BGRA (what UpdateLayeredWindow expects), scaled to <paramref name="height"/>
    /// pixels tall, keeping its proportions.
    /// </summary>
    private static (byte[] Pixels, int Width) Decode(string path, int height)
    {
        return Task.Run(async () =>
        {
            await using var file = File.OpenRead(path);
            using IRandomAccessStream stream = file.AsRandomAccessStream();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var width = (int)Math.Round((double)height * decoder.PixelWidth / decoder.PixelHeight);
            var transform = new BitmapTransform
            {
                ScaledWidth = (uint)width,
                ScaledHeight = (uint)height,
                InterpolationMode = BitmapInterpolationMode.Fant,
            };
            var data = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, transform,
                ExifOrientationMode.IgnoreExifOrientation, ColorManagementMode.DoNotColorManage);
            return (data.DetachPixelData(), width);
        }).GetAwaiter().GetResult();
    }

    // --- Win32 ---------------------------------------------------------------------------------

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_EX_LAYERED = 0x00080000;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_TOPMOST = 0x00000008;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int SW_SHOWNOACTIVATE = 4;
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    private const byte AC_SRC_OVER = 0;
    private const byte AC_SRC_ALPHA = 1;
    private const int ULW_ALPHA = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct SIZE { public int cx, cy; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BITMAPINFOHEADER
    {
        public int biSize, biWidth, biHeight;
        public short biPlanes, biBitCount;
        public int biCompression, biSizeImage, biXPelsPerMeter, biYPelsPerMeter, biClrUsed, biClrImportant;
    }

    [LibraryImport("user32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateWindowExW(int exStyle, string className, string windowName, int style,
        int x, int y, int width, int height, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DestroyWindow(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ShowWindow(IntPtr hwnd, int command);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool UpdateLayeredWindow(IntPtr hwnd, IntPtr dstDc, ref POINT dst, ref SIZE size,
        IntPtr srcDc, ref POINT src, int key, ref BLENDFUNCTION blend, int flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT point);

    [LibraryImport("user32.dll")]
    private static partial IntPtr MonitorFromPoint(POINT point, uint flags);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetMonitorInfoW(IntPtr monitor, ref MONITORINFO info);

    [LibraryImport("shcore.dll")]
    private static partial int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [LibraryImport("user32.dll")]
    private static partial IntPtr GetDC(IntPtr hwnd);

    [LibraryImport("user32.dll")]
    private static partial int ReleaseDC(IntPtr hwnd, IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateCompatibleDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr CreateDIBSection(IntPtr dc, ref BITMAPINFOHEADER header, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    [LibraryImport("gdi32.dll")]
    private static partial IntPtr SelectObject(IntPtr dc, IntPtr obj);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteObject(IntPtr obj);
}
