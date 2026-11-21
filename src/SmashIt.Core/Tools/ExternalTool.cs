using System.Diagnostics;

namespace SmashIt.Core.Tools;

public sealed class ExternalToolException(string message) : Exception(message);

/// <summary>Locates and runs the third-party command-line tools bundled with Smash It.</summary>
public static class ExternalTool
{
    /// <summary>Overrides the tools folder (useful for development and tests).</summary>
    public const string ToolsDirectoryVariable = "SMASHIT_TOOLS_DIR";

    /// <summary>Full path of <paramref name="executable"/>, or <c>null</c> when it is not bundled.</summary>
    public static string? Find(string executable)
    {
        var candidates = new List<string>();
        if (Environment.GetEnvironmentVariable(ToolsDirectoryVariable) is { Length: > 0 } custom)
            candidates.Add(custom);
        candidates.Add(Path.Combine(AppContext.BaseDirectory, "tools"));

        return candidates
            .Select(dir => Path.Combine(dir, executable))
            .FirstOrDefault(File.Exists);
    }

    /// <summary>Runs a tool without a console window, at below-normal priority, and throws if it fails.</summary>
    public static async Task RunAsync(string executable, IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var result = await RunRawAsync(executable, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode != 0) throw Failure(executable, result);
    }

    public static ExternalToolException Failure(string executable, ToolResult result)
    {
        var errors = result.StandardError.Trim();
        var tail = errors.Length > 300 ? "…" + errors[^300..] : errors;
        return new ExternalToolException(
            L10n.T(
                $"{Path.GetFileName(executable)} failed (code {result.ExitCode}){(tail.Length > 0 ? ": " + tail : ".")}",
                $"{Path.GetFileName(executable)} a échoué (code {result.ExitCode}){(tail.Length > 0 ? " : " + tail : ".")}"));
    }

    /// <summary>
    /// Runs a tool and returns its exit code and output, whatever the exit code. With <paramref name="onOutputLine"/>,
    /// standard output is handed over line by line as the tool writes it (progress), instead of being returned.
    /// </summary>
    public static async Task<ToolResult> RunRawAsync(
        string executable, IEnumerable<string> arguments, CancellationToken cancellationToken, Action<string>? onOutputLine = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);

        using var process = Process.Start(startInfo)
            ?? throw new ExternalToolException(L10n.T(
                $"Could not start {Path.GetFileName(executable)}.",
                $"Impossible de lancer {Path.GetFileName(executable)}."));
        try { process.PriorityClass = ProcessPriorityClass.BelowNormal; }
        catch (InvalidOperationException) { } // already exited

        var stdout = onOutputLine is null
            ? process.StandardOutput.ReadToEndAsync(CancellationToken.None)
            : ReadLinesAsync(process.StandardOutput, onOutputLine);
        var stderr = process.StandardError.ReadToEndAsync(CancellationToken.None);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
            throw;
        }

        return new ToolResult(process.ExitCode, await stdout.ConfigureAwait(false), await stderr.ConfigureAwait(false));
    }

    private static async Task<string> ReadLinesAsync(StreamReader reader, Action<string> onLine)
    {
        while (await reader.ReadLineAsync().ConfigureAwait(false) is { } line) onLine(line);
        return "";
    }
}

public sealed record ToolResult(int ExitCode, string StandardOutput, string StandardError);
