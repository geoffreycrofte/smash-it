using System.Runtime.InteropServices;
using Microsoft.Win32;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;

namespace SmashIt.Core.Shell;

/// <summary>
/// The "Compress with Smash It" entry of the Explorer right-click menu, for the current user only
/// (HKEY_CURRENT_USER\Software\Classes, no admin rights).
/// </summary>
/// <remarks>
/// A classic shell verb: Windows 11 shows it under "Show more options", since the new top-level menu only
/// accepts packaged, signed apps. Explorer starts the command once per selected item; the app forwards those
/// launches to its first window.
/// </remarks>
public sealed class ExplorerContextMenu(RegistryKey classesRoot) : IContextMenu
{
    public const string VerbName = "SmashIt.Compress";

    /// <summary>The menu of the current user.</summary>
    public static ExplorerContextMenu ForCurrentUser() => new(Registry.CurrentUser.CreateSubKey(@"Software\Classes"));

    /// <summary>File extensions (with the dot) that at least one engine can compress, plus their aliases.</summary>
    public static IReadOnlyList<string> Extensions { get; } = ComputeExtensions();

    private static IReadOnlyList<string> ComputeExtensions()
    {
        var compressors = SmashItEngine.CreateDefaultPipeline().Compressors;
        CompressionOptions[] modes = [new() { Mode = CompressionMode.Lossless }, new() { Mode = CompressionMode.Lossy }];
        return MediaFormatInfo.KnownExtensions
            .Where(extension =>
            {
                var format = MediaFormatInfo.FromExtension("x" + extension);
                var output = MediaFormatInfo.DefaultOutputFor(format);
                return compressors.Any(c => modes.Any(m => c.CanHandle(format, output, m)));
            })
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IEnumerable<string> VerbKeys =>
        Extensions.Select(e => $@"SystemFileAssociations\{e}\shell\{VerbName}").Append($@"Directory\shell\{VerbName}");

    public static string CommandFor(string executable) => $"\"{executable}\" \"%1\"";

    /// <summary>Adds or refreshes the entry for every supported file type and for folders.</summary>
    public void Register(string executable, string label)
    {
        foreach (var path in VerbKeys)
        {
            using var verb = classesRoot.CreateSubKey(path);
            verb.SetValue("MUIVerb", label);
            verb.SetValue("Icon", $"\"{executable}\",0");
            // Shown for any number of selected items (the default hides it above 15).
            verb.SetValue("MultiSelectModel", "Player");
            using var command = verb.CreateSubKey("command");
            command.SetValue(null, CommandFor(executable));
        }
        NotifyExplorer();
    }

    public void Unregister()
    {
        foreach (var path in VerbKeys)
        {
            classesRoot.DeleteSubKeyTree(path, throwOnMissingSubKey: false);
            DeleteEmptyParents(path);
        }
        NotifyExplorer();
    }

    /// <summary>
    /// Removes the "shell" and ".ext" keys <see cref="Register"/> may have created, when nothing else lives in
    /// them. "SystemFileAssociations" itself is left alone.
    /// </summary>
    private void DeleteEmptyParents(string verbPath)
    {
        var parent = verbPath[..verbPath.LastIndexOf('\\')];
        while (parent.Contains('\\'))
        {
            using (var key = classesRoot.OpenSubKey(parent))
            {
                if (key is null) { parent = parent[..parent.LastIndexOf('\\')]; continue; }
                if (key.SubKeyCount > 0 || key.ValueCount > 0) return;
            }
            classesRoot.DeleteSubKey(parent, throwOnMissingSubKey: false);
            parent = parent[..parent.LastIndexOf('\\')];
        }
    }

    /// <summary>True when the entry exists (checked on the folder verb, written with every file type).</summary>
    public bool IsRegistered => RegisteredCommand() is not null;

    /// <summary>True when the entry exists and starts <paramref name="executable"/> (false after the app moved).</summary>
    public bool IsRegisteredFor(string executable) =>
        string.Equals(RegisteredCommand(), CommandFor(executable), StringComparison.OrdinalIgnoreCase);

    /// <summary>The program the entry starts, or <c>null</c> when there is no entry.</summary>
    public string? RegisteredExecutable =>
        RegisteredCommand() is ['"', .. var rest] && rest.IndexOf('"') is > 0 and var end ? rest[..end] : null;

    private string? RegisteredCommand()
    {
        using var command = classesRoot.OpenSubKey($@"Directory\shell\{VerbName}\command");
        return command?.GetValue(null) as string;
    }

    /// <summary>Tells Explorer the file associations changed, so the menu updates without a restart.</summary>
    private static void NotifyExplorer() => SHChangeNotify(0x08000000 /* SHCNE_ASSOCCHANGED */, 0, 0, 0);

    [DllImport("shell32.dll")]
    private static extern void SHChangeNotify(int eventId, uint flags, nint item1, nint item2);
}
