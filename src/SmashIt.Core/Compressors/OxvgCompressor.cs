using System.Globalization;
using System.Text.Json;
using System.Xml;
using SmashIt.Core.Compression;
using SmashIt.Core.Formats;
using SmashIt.Core.Tools;

namespace SmashIt.Core.Compressors;

/// <summary>
/// oxvg: SVG optimizer (MIT), a Rust port of SVGO's default preset. Removes what editors leave behind (comments,
/// metadata, editor data, hidden or empty elements, default values), rounds coordinates to 3 decimals, and
/// rewrites shapes and paths more compactly. The image looks the same.
/// </summary>
/// <remarks>
/// Kept on purpose: IDs (another file or a stylesheet may point to them), the <c>viewBox</c> (scaling), and the
/// title (screen readers). oxvg is young, so its output is only used when it is a well-formed SVG with the same
/// size as the original (<see cref="CheckSameDocument"/>); otherwise the original stays.
/// </remarks>
public sealed partial class OxvgCompressor() : ToolCompressor("oxvg.exe")
{
    public override string Id => "oxvg";

    public override string DisplayName => "oxvg";

    public override bool CanHandle(MediaFormat input, MediaFormat output, CompressionOptions options) =>
        input == MediaFormat.Svg && output == MediaFormat.Svg;

    /// <summary>The oxvg configuration: SVGO's default preset, minus the jobs that could break a file.</summary>
    public static string Configuration(CompressionOptions options)
    {
        var omit = new List<string> { "cleanup_ids" };
        // RDF (license, author…) counts as editor data for oxvg: keeping metadata keeps that too.
        if (!options.StripMetadata) omit.AddRange(["remove_comments", "remove_metadata", "remove_editors_n_s_data"]);
        return JsonSerializer.Serialize(new { optimise = new { extends = "default", jobs = new { }, omit } });
    }

    public override async Task CompressAsync(CompressionJob job, CancellationToken cancellationToken)
    {
        // oxvg treats the glyphs of an SVG font (an old icon font format) as unused and removes them all.
        if (IsSvgFont(job.InputPath))
            throw new NotSupportedException(L10n.T(
                "SVG fonts are left as they are.",
                "Les polices SVG sont laissées telles quelles."));

        var config = job.OutputPath + ".oxvg.json";
        try
        {
            await File.WriteAllTextAsync(config, Configuration(job.Options), cancellationToken);
            // An explicit config, so no oxvg settings file lying around changes the result.
            var result = await ExternalTool.RunRawAsync(
                ToolPath ?? throw new ExternalToolException(L10n.T("oxvg not found.", "oxvg est introuvable.")),
                ["optimise", "--config", config, "--quiet", "--no-ignore", "--hidden", "--output", job.OutputPath, job.InputPath],
                cancellationToken);
            // oxvg reports a document it can't read on stderr and still exits with 0.
            if (result.ExitCode != 0 || !File.Exists(job.OutputPath) || new FileInfo(job.OutputPath).Length == 0)
                throw ExternalTool.Failure(ToolPath, result with { ExitCode = result.ExitCode == 0 ? -1 : result.ExitCode });

            if (CheckSameDocument(job.InputPath, job.OutputPath) is { } problem)
            {
                File.Delete(job.OutputPath);
                throw new ExternalToolException(L10n.T(
                    $"oxvg's result was set aside ({problem}): the original is kept.",
                    $"Le résultat d'oxvg a été écarté ({problem}) : l'original est conservé."));
            }
        }
        finally
        {
            File.Delete(config);
        }
    }

    /// <summary>
    /// <c>null</c> when <paramref name="output"/> is a well-formed SVG with the same width, height and viewBox as
    /// <paramref name="input"/>; otherwise what is wrong.
    /// </summary>
    public static string? CheckSameDocument(string input, string output)
    {
        XmlElement? before, after;
        try
        {
            before = Load(input);
        }
        catch (XmlException)
        {
            return null; // the original can't be checked either: nothing to compare with
        }
        try
        {
            after = Load(output);
        }
        catch (XmlException ex)
        {
            return L10n.T($"not a valid SVG: {ex.Message}", $"SVG invalide : {ex.Message}");
        }

        if (after is not { LocalName: "svg" })
            return L10n.T("no <svg> root", "pas de racine <svg>");
        if (before is null) return null;

        // Shapes may be merged or turned into paths, but an image never ends up with nothing to draw.
        if (CountDrawn(before) > 0 && CountDrawn(after) == 0)
            return L10n.T("its content disappeared", "son contenu a disparu");

        foreach (var attribute in new[] { "width", "height", "viewBox" })
        {
            if (!SameValue(before.GetAttribute(attribute), after.GetAttribute(attribute)))
                return L10n.T($"its {attribute} changed", $"son {attribute} a changé");
        }
        return null;
    }

    private static readonly HashSet<string> Drawn =
        ["path", "rect", "circle", "ellipse", "line", "polyline", "polygon", "text", "image", "use", "glyph"];

    private static int CountDrawn(XmlElement root) =>
        root.GetElementsByTagName("*").Cast<XmlElement>().Count(e => Drawn.Contains(e.LocalName));

    /// <summary>True for an SVG font: a &lt;font&gt; with glyphs, as icon font generators produced.</summary>
    public static bool IsSvgFont(string path)
    {
        try
        {
            return Load(path) is { } root &&
                root.GetElementsByTagName("*").Cast<XmlElement>().Any(e => e.LocalName is "font" or "glyph");
        }
        catch (XmlException)
        {
            return false;
        }
    }

    private static XmlElement? Load(string path)
    {
        var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Ignore, XmlResolver = null };
        using var reader = XmlReader.Create(path, settings);
        var document = new XmlDocument { XmlResolver = null };
        document.Load(reader);
        return document.DocumentElement;
    }

    /// <summary>Same text, or the same numbers and units once rounded ("100.000px" = "100", "0 0 24 24" = "0,0,24,24").</summary>
    internal static bool SameValue(string a, string b)
    {
        if (a.Trim() == b.Trim()) return true;
        var left = Lengths(a);
        var right = Lengths(b);
        if (left is null || right is null || left.Count != right.Count) return false;
        return left.Zip(right).All(pair =>
            pair.First.Unit == pair.Second.Unit &&
            Math.Abs(pair.First.Value - pair.Second.Value) <= 0.001 * Math.Max(1, Math.Abs(pair.First.Value)));
    }

    private static List<(double Value, string Unit)>? Lengths(string value)
    {
        var lengths = new List<(double, string)>();
        foreach (var part in value.Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            var match = LengthPattern().Match(part);
            if (!match.Success) return null;
            // "px" is the default unit: "240px" and "240" are the same size.
            var unit = match.Groups[2].Value.ToLowerInvariant();
            lengths.Add((double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture), unit == "px" ? "" : unit));
        }
        return lengths;
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^([-+]?(?:\d+\.?\d*|\.\d+)(?:[eE][-+]?\d+)?)([a-zA-Z%]*)$")]
    private static partial System.Text.RegularExpressions.Regex LengthPattern();
}
