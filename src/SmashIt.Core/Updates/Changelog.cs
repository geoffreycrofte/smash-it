using System.Text.RegularExpressions;

namespace SmashIt.Core.Updates;

/// <summary>One "## v1.2.0" section of CHANGELOG.md.</summary>
public sealed record ChangelogSection(AppVersion Version, string Body);

/// <summary>
/// Reads CHANGELOG.md: one level-2 heading per version, newest first ("## v0.2.0", "## v0.2.0 · 1er décembre 2026",
/// "## [0.2.0] - 2026-12-01"). Other level-2 headings ("## Next version") are drafts and are left out.
/// </summary>
public static partial class Changelog
{
    public const string FileName = "CHANGELOG.md";

    public static IReadOnlyList<ChangelogSection> Parse(string markdown)
    {
        var sections = new List<ChangelogSection>();
        AppVersion? version = null;
        var body = new List<string>();

        void Close()
        {
            if (version is not null) sections.Add(new ChangelogSection(version, string.Join('\n', body).Trim()));
            body.Clear();
        }

        foreach (var line in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                Close();
                version = VersionHeading().Match(line) is { Success: true } m && AppVersion.TryParse(m.Groups[1].Value, out var v)
                    ? v
                    : null; // not a version: its lines are skipped until the next heading
            }
            else if (line.StartsWith("# ", StringComparison.Ordinal))
            {
                Close();
                version = null; // the document title
            }
            else if (version is not null)
            {
                body.Add(line);
            }
        }
        Close();
        return sections;
    }

    /// <summary>
    /// The sections after <paramref name="after"/> (the version the user has, or had before updating) up to
    /// <paramref name="upTo"/> included, newest first: everything listed above "## v&lt;after&gt;".
    /// </summary>
    public static IReadOnlyList<ChangelogSection> Between(IEnumerable<ChangelogSection> sections, AppVersion after, AppVersion upTo) =>
        sections.Where(s => s.Version > after && s.Version <= upTo).OrderByDescending(s => s.Version).ToList();

    /// <summary>The body of one version's section (the release text), or <c>null</c>.</summary>
    public static string? SectionFor(string markdown, AppVersion version) =>
        Parse(markdown).FirstOrDefault(s => s.Version.CompareTo(version) == 0)?.Body;

    /// <summary>
    /// Joins the lines that only continue the one above (a list item or a paragraph wrapped in the file), so each
    /// returned line is a whole heading, list item or paragraph. Blank lines are dropped.
    /// </summary>
    public static IReadOnlyList<string> LogicalLines(string markdown)
    {
        var lines = new List<string>();
        var previousBlank = true;
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0)
            {
                previousBlank = true;
                continue;
            }
            // A heading never continues on the next line; anything else that isn't a new block does.
            var continues = !previousBlank && lines.Count > 0 && !StartsBlock().IsMatch(line)
                && !lines[^1].TrimStart().StartsWith('#');
            if (continues)
                lines[^1] = lines[^1] + " " + line.Trim();
            else
                lines.Add(line);
            previousBlank = false;
        }
        return lines;
    }

    /// <summary>A line that opens its own block: heading, list item, quote, rule or HTML comment.</summary>
    [GeneratedRegex(@"^\s*(#{1,6}\s|[-*+]\s|\d+\.\s|>|---|\*\*\*|<!--)")]
    private static partial Regex StartsBlock();

    [GeneratedRegex(@"^##\s+\[?v?(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\]?(?:\s|$)")]
    private static partial Regex VersionHeading();
}
