using System.Globalization;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Pdf;

/// <summary>Calls to the qpdf command-line tool (Apache-2.0).</summary>
internal sealed class Qpdf(string executable)
{
    // qpdf exit codes: 0 = OK, 2 = errors, 3 = warnings only.
    private const int Warnings = 3;

    public static Qpdf? Find() => ExternalTool.Find("qpdf.exe") is { } path ? new Qpdf(path) : null;

    public string Executable => executable;

    /// <summary>Runs qpdf; warnings are tolerated (many real-world PDFs have harmless ones), errors throw.</summary>
    public async Task<string> RunAsync(IEnumerable<string> arguments, CancellationToken cancellationToken)
    {
        var result = await ExternalTool.RunRawAsync(executable, arguments, cancellationToken).ConfigureAwait(false);
        if (result.ExitCode is not (0 or Warnings)) throw ExternalTool.Failure(executable, result);
        return result.StandardOutput;
    }

    public async Task<bool> IsEncryptedAsync(string pdf, CancellationToken cancellationToken)
    {
        // --is-encrypted: 0 = encrypted, 2 = not encrypted.
        var result = await ExternalTool.RunRawAsync(executable, ["--is-encrypted", pdf], cancellationToken).ConfigureAwait(false);
        return result.ExitCode == 0;
    }

    public async Task<int> CountPagesAsync(string pdf, CancellationToken cancellationToken)
    {
        var output = await RunAsync(["--show-npages", pdf], cancellationToken).ConfigureAwait(false);
        return int.TryParse(output.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var pages)
            ? pages
            : throw new InvalidDataException(L10n.T("qpdf could not count the pages.", "qpdf n'a pas pu compter les pages."));
    }

    /// <summary>Structural check of the whole file, including every stream's encoding.</summary>
    public Task CheckAsync(string pdf, CancellationToken cancellationToken) =>
        RunAsync(["--check", pdf], cancellationToken);
}
