using SmashIt.Core.Formats;

namespace SmashIt.Core.IO;

public static class InputCollector
{
    /// <summary>
    /// Expands files and folders (recursively) into the list of files Smash It knows about.
    /// Files given explicitly are always kept; files found in folders are filtered by extension.
    /// </summary>
    public static IEnumerable<string> Expand(IEnumerable<string> paths)
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
                var files = Directory.EnumerateFiles(path, "*", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
                });
                foreach (var file in files)
                {
                    if (MediaFormatInfo.FromExtension(file) == MediaFormat.Unknown) continue;
                    var full = Path.GetFullPath(file);
                    if (seen.Add(full)) yield return full;
                }
            }
        }
    }
}
