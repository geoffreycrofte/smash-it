using SmashIt.Core.Formats;

namespace SmashIt.Core.IO;

public static class InputCollector
{
    /// <summary>
    /// Expands files and folders (recursively) into the list of files Smash It knows about.
    /// Files given explicitly are always kept; files found in folders are filtered by extension.
    /// </summary>
    /// <param name="skippedFolderName">
    /// Subfolders with this name are not entered (e.g. "compressed": the results of an earlier run would
    /// otherwise be compressed again, into compressed\compressed).
    /// </param>
    public static IEnumerable<string> Expand(IEnumerable<string> paths, string? skippedFolderName = null)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                var full = Path.GetFullPath(path);
                if (seen.Add(full)) yield return full;
            }
            else if (Directory.Exists(path))
            {
                var root = Path.GetFullPath(path);
                var files = Directory.EnumerateFiles(root, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                });
                foreach (var file in files)
                {
                    if (MediaFormatInfo.FromExtension(file) == MediaFormat.Unknown) continue;
                    if (skippedFolderName is not null && IsInsideFolderNamed(root, file, skippedFolderName)) continue;
                    var full = Path.GetFullPath(file);
                    if (seen.Add(full)) yield return full;
                }
            }
        }
    }

    /// <summary>True when a folder between <paramref name="root"/> and the file is called <paramref name="name"/>.</summary>
    private static bool IsInsideFolderNamed(string root, string file, string name)
    {
        var relativeFolder = Path.GetDirectoryName(Path.GetRelativePath(root, file));
        return !string.IsNullOrEmpty(relativeFolder) &&
            relativeFolder.Split(Path.DirectorySeparatorChar).Contains(name, StringComparer.OrdinalIgnoreCase);
    }
}
