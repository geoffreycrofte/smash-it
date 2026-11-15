using Microsoft.VisualBasic.FileIO;

namespace SmashIt.Core.IO;

internal static class FileOutput
{
    /// <summary>
    /// Puts <paramref name="newFile"/> in place of <paramref name="original"/>.
    /// The new file is first moved next to the original (same volume) so the final swap cannot fail half-way.
    /// </summary>
    public static void Replace(string original, string newFile, bool sendOriginalToRecycleBin)
    {
        var staging = original + ".smashit-tmp";
        File.Move(newFile, staging, overwrite: true);
        try
        {
            if (sendOriginalToRecycleBin)
                FileSystem.DeleteFile(original, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
            else
                File.Delete(original);

            File.Move(staging, original);
        }
        catch
        {
            if (File.Exists(staging) && File.Exists(original)) File.Delete(staging);
            throw;
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
