using SmashIt.Core.Compression;

namespace SmashIt.Core.IO;

internal static class FileOutput
{
    /// <summary>
    /// Puts <paramref name="newFile"/> in place of <paramref name="original"/>.
    /// The new file is first moved next to the original (same volume) so the final swap cannot fail half-way.
    /// </summary>
    public static void Replace(string original, string newFile, OriginalHandling handling)
    {
        var staging = original + ".smashit-tmp";
        File.Move(newFile, staging, overwrite: true);
        try
        {
            DisposeOriginal(original, handling);
            File.Move(staging, original);
        }
        catch
        {
            if (File.Exists(staging) && File.Exists(original)) File.Delete(staging);
            throw;
        }
    }

    /// <summary>Sends the original to the Recycle Bin or deletes it, depending on <paramref name="handling"/>.</summary>
    public static void DisposeOriginal(string original, OriginalHandling handling)
    {
        switch (handling)
        {
            case OriginalHandling.RecycleBin:
                Platform.MoveToTrash(original);
                break;
            case OriginalHandling.Overwrite:
                File.Delete(original);
                break;
        }
    }

    /// <summary>Returns <paramref name="path"/>, or "name (2).ext", "name (3).ext"… if it already exists.</summary>
    public static string UniquePath(string path)
    {
        if (!File.Exists(path)) return path;

        var directory = Path.GetDirectoryName(path) ?? string.Empty;
        var name = Path.GetFileNameWithoutExtension(path);
        var extension = Path.GetExtension(path);
        for (var i = 2; ; i++)
        {
            var candidate = Path.Combine(directory, $"{name} ({i}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }
}
