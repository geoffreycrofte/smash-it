using System.Diagnostics;

namespace SmashIt.App.Services;

/// <summary>
/// Keeps a temporary copy of each image before it is compressed, so the before/after preview still works
/// once the original has gone to the Recycle Bin. One folder per app instance, removed on exit.
/// </summary>
internal static class PreviewCache
{
    private static readonly string Root = Path.Combine(Path.GetTempPath(), "SmashIt", "Preview");
    private static readonly string Folder = Path.Combine(Root, Environment.ProcessId.ToString());

    /// <summary>Copies <paramref name="path"/> into the cache; <c>null</c> if that failed (no preview then).</summary>
    public static string? Keep(string path)
    {
        try
        {
            Directory.CreateDirectory(Folder);
            var copy = Path.Combine(Folder, Guid.NewGuid().ToString("N") + Path.GetExtension(path));
            File.Copy(path, copy);
            return copy;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    public static void Forget(string? copy)
    {
        if (copy is null) return;
        try { File.Delete(copy); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>Removes this instance's folder and the ones left by instances that are no longer running.</summary>
    public static void Clean(bool includeCurrent)
    {
        if (!Directory.Exists(Root)) return;
        foreach (var folder in Directory.EnumerateDirectories(Root))
        {
            var isCurrent = string.Equals(folder, Folder, StringComparison.OrdinalIgnoreCase);
            if (isCurrent && !includeCurrent) continue;
            if (!isCurrent && int.TryParse(Path.GetFileName(folder), out var pid) && IsRunning(pid)) continue;
            try { Directory.Delete(folder, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    private static bool IsRunning(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
