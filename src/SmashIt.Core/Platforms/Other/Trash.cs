using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace SmashIt.Core.IO;

/// <summary>
/// Moves files to the trash on macOS (the Finder's, with "Put Back") and on Linux (the freedesktop.org trash that
/// GNOME, KDE and the other desktops share).
/// </summary>
internal static partial class Trash
{
    public static void Move(string path)
    {
        var full = Path.GetFullPath(path);
        if (!File.Exists(full)) throw new FileNotFoundException(null, full);

        if (OperatingSystem.IsMacOS()) MacOS.Move(full);
        else FreeDesktop.Move(full);
    }

    /// <summary>NSFileManager's <c>trashItemAtURL:resultingItemURL:error:</c>, through the Objective-C runtime.</summary>
    private static partial class MacOS
    {
        private const string ObjC = "/usr/lib/libobjc.A.dylib";

        public static void Move(string path)
        {
            // Loads Foundation, where NSFileManager and NSURL live.
            NativeLibrary.Load("/System/Library/Frameworks/Foundation.framework/Foundation");

            var utf8 = Encoding.UTF8.GetBytes(path + "\0");
            var nsString = SendPointer(Class("NSString"), Selector("stringWithUTF8String:"), utf8);
            var url = SendPointer(Class("NSURL"), Selector("fileURLWithPath:"), nsString);
            var manager = Send(Class("NSFileManager"), Selector("defaultManager"));
            if (!SendTrash(manager, Selector("trashItemAtURL:resultingItemURL:error:"), url, IntPtr.Zero, out var error))
            {
                var description = error == IntPtr.Zero ? null : Utf8(Send(Send(error, Selector("localizedDescription")), Selector("UTF8String")));
                throw new IOException(L10n.T(
                    $"Couldn't move {Path.GetFileName(path)} to the Trash: {description}",
                    $"Impossible de mettre {Path.GetFileName(path)} à la corbeille : {description}"));
            }
        }

        private static IntPtr Class(string name) => objc_getClass(name);

        private static IntPtr Selector(string name) => sel_registerName(name);

        private static string? Utf8(IntPtr text) => Marshal.PtrToStringUTF8(text);

        [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr objc_getClass(string name);

        [LibraryImport(ObjC, StringMarshalling = StringMarshalling.Utf8)]
        private static partial IntPtr sel_registerName(string name);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial IntPtr Send(IntPtr receiver, IntPtr selector);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial IntPtr SendPointer(IntPtr receiver, IntPtr selector, byte[] argument);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        private static partial IntPtr SendPointer(IntPtr receiver, IntPtr selector, IntPtr argument);

        [LibraryImport(ObjC, EntryPoint = "objc_msgSend")]
        [return: MarshalAs(UnmanagedType.U1)]
        private static partial bool SendTrash(IntPtr receiver, IntPtr selector, IntPtr url, IntPtr resultingUrl, out IntPtr error);
    }

    /// <summary>The freedesktop.org Trash specification 1.0: files/ for the file, info/ for where it came from.</summary>
    internal static class FreeDesktop
    {
        public static void Move(string path) => Move(path, HomeTrash());

        /// <param name="home">The home trash (a temporary folder in the tests).</param>
        internal static void Move(string path, string home)
        {
            try
            {
                MoveInto(home, path, relativeTo: null);
            }
            catch (IOException) when (TopDirectoryTrash(path) is { } other)
            {
                // Another disk: its own trash, so nothing is copied across (paths there are relative to the disk).
                MoveInto(other.Trash, path, relativeTo: other.TopDirectory);
            }
        }

        /// <summary>$XDG_DATA_HOME/Trash, by default ~/.local/share/Trash.</summary>
        public static string HomeTrash()
        {
            var data = Environment.GetEnvironmentVariable("XDG_DATA_HOME") is { Length: > 0 } custom
                ? custom
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".local", "share");
            return Path.Combine(data, "Trash");
        }

        private static void MoveInto(string trash, string path, string? relativeTo)
        {
            var files = Path.Combine(trash, "files");
            var info = Path.Combine(trash, "info");
            Directory.CreateDirectory(files);
            Directory.CreateDirectory(info);

            // The info file is created first, exclusively: it reserves the name.
            var name = Path.GetFileName(path);
            string infoPath;
            FileStream stream;
            for (var i = 1; ; i++)
            {
                var candidate = i == 1 ? name : $"{Path.GetFileNameWithoutExtension(name)}.{i}{Path.GetExtension(name)}";
                infoPath = Path.Combine(info, candidate + ".trashinfo");
                try
                {
                    stream = new FileStream(infoPath, FileMode.CreateNew, FileAccess.Write);
                    name = candidate;
                    break;
                }
                catch (IOException) when (File.Exists(infoPath))
                {
                }
            }

            using (stream)
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                var original = relativeTo is null ? path : Path.GetRelativePath(relativeTo, path);
                writer.Write($"[Trash Info]\nPath={Escape(original)}\nDeletionDate={DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)}\n");
            }

            try
            {
                // Rename only (same disk): File.Move would fall back to copying across disks.
                Rename(path, Path.Combine(files, name));
            }
            catch
            {
                File.Delete(infoPath);
                throw;
            }
        }

        /// <summary>$topdir/.Trash/$uid when an administrator set it up (sticky, not a link), else $topdir/.Trash-$uid.</summary>
        private static (string TopDirectory, string Trash)? TopDirectoryTrash(string path)
        {
            var top = MountPoint(path);
            if (top is null) return null;
            var uid = getuid().ToString(CultureInfo.InvariantCulture);
            var shared = Path.Combine(top, ".Trash");
            var sharedInfo = new DirectoryInfo(shared);
            if (!OperatingSystem.IsWindows() && sharedInfo.Exists && sharedInfo.LinkTarget is null &&
                (File.GetUnixFileMode(shared) & UnixFileMode.StickyBit) != 0)
                return (top, Path.Combine(shared, uid));
            return (top, Path.Combine(top, ".Trash-" + uid));
        }

        /// <summary>The deepest mount point holding <paramref name="path"/> (from /proc/self/mounts).</summary>
        private static string? MountPoint(string path)
        {
            const string mounts = "/proc/self/mounts";
            if (!File.Exists(mounts)) return null;
            return File.ReadLines(mounts)
                .Select(line => line.Split(' '))
                .Where(fields => fields.Length > 1)
                .Select(fields => fields[1].Replace("\\040", " ", StringComparison.Ordinal))
                .Where(point => point == "/" || path.StartsWith(point.TrimEnd('/') + "/", StringComparison.Ordinal))
                .MaxBy(point => point.Length);
        }

        /// <summary>The trashinfo Path: percent-encoded like a URL path, "/" kept.</summary>
        internal static string Escape(string path)
        {
            var builder = new StringBuilder();
            foreach (var b in Encoding.UTF8.GetBytes(path))
            {
                var c = (char)b;
                if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '/' or '-' or '_' or '.' or '~')
                    builder.Append(c);
                else
                    builder.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
            }
            return builder.ToString();
        }

        private static void Rename(string from, string to)
        {
            if (rename(from, to) != 0)
            {
                var errno = Marshal.GetLastPInvokeError();
                throw new IOException(L10n.T(
                    $"Couldn't move {Path.GetFileName(from)} to the trash (error {errno}).",
                    $"Impossible de mettre {Path.GetFileName(from)} à la corbeille (erreur {errno})."), errno);
            }
        }
    }

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int rename(string from, string to);

    [LibraryImport("libc")]
    private static partial uint getuid();
}
