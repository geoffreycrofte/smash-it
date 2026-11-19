using System.Runtime.InteropServices;
using Microsoft.Windows.AppLifecycle;
using Windows.ApplicationModel.Activation;

namespace SmashIt.App.Services;

/// <summary>
/// One Smash It window. Explorer starts the app once per selected file ("Compress with Smash It" on 40 photos
/// means 40 launches): every launch after the first hands its files to the first one and exits.
/// </summary>
internal static partial class SingleInstance
{
    private const string Key = "SmashIt.Main";

    private static readonly Lock Gate = new();
    private static readonly List<string[]> Pending = [];
    private static Action<string[]>? _receiver;

    /// <summary>
    /// True for the first instance, which goes on to show the window. Otherwise the launch, files included, has
    /// been forwarded to the running instance and this process should exit.
    /// </summary>
    public static bool TryBecomePrimary()
    {
        var primary = AppInstance.FindOrRegisterForKey(Key);
        if (primary.IsCurrent)
        {
            primary.Activated += OnActivated;
            return true;
        }

        try
        {
            // Lets the running window come to the front when it receives the files.
            AllowSetForegroundWindow(primary.ProcessId);
            var launch = AppInstance.GetCurrent().GetActivatedEventArgs();
            // Not on this STA thread: the redirection must not block COM calls it depends on.
            Task.Run(() => primary.RedirectActivationToAsync(launch).AsTask()).Wait();
            return false;
        }
        catch (Exception ex) when (ex is AggregateException or COMException)
        {
            // The other window is closing: open one here rather than lose the files.
            return true;
        }
    }

    /// <summary>
    /// Called once the window is ready. Launches forwarded earlier are delivered right away, later ones as they
    /// come (on a background thread: <paramref name="receiver"/> dispatches to the UI).
    /// </summary>
    public static void OnLaunchForwarded(Action<string[]> receiver)
    {
        lock (Gate)
        {
            _receiver = receiver;
            foreach (var paths in Pending) receiver(paths);
            Pending.Clear();
        }
    }

    private static void OnActivated(object? sender, AppActivationArguments e)
    {
        if (e.Data is not ILaunchActivatedEventArgs launch) return;
        var paths = PathsFromCommandLine(launch.Arguments);
        lock (Gate)
        {
            if (_receiver is { } receiver) receiver(paths);
            else Pending.Add(paths);
        }
    }

    /// <summary>The file and folder paths of a command line, without the program itself or options.</summary>
    internal static string[] PathsFromCommandLine(string? commandLine)
    {
        if (string.IsNullOrWhiteSpace(commandLine)) return [];
        var argv = CommandLineToArgvW(commandLine, out var count);
        if (argv == 0) return [];
        try
        {
            var items = new string[count];
            for (var i = 0; i < count; i++) items[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size)) ?? "";
            return items
                .Where((item, index) => !(index == 0 && item.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))
                .Where(item => item.Length > 0 && !item.StartsWith("--", StringComparison.Ordinal))
                .ToArray();
        }
        finally
        {
            LocalFree(argv);
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial nint CommandLineToArgvW(string commandLine, out int count);

    [LibraryImport("kernel32.dll")]
    private static partial nint LocalFree(nint memory);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool AllowSetForegroundWindow(uint processId);
}
