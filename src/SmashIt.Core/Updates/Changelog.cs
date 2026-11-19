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

    [GeneratedRegex(@"^##\s+\[?v?(\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?)\]?(?:\s|$)")]
    private static partial Regex VersionHeading();
}
