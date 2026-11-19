using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using SmashIt.Core.Updates;

namespace SmashIt.App.Services;

/// <summary>
/// Shows GitHub release notes. They are Markdown; only what release notes use is rendered (headings, lists,
/// bold, code, links), the rest stays as plain text.
/// </summary>
public static partial class ReleaseNotesView
{
    public static UIElement Build(IReadOnlyList<ReleaseInfo> releases)
    {
        var panel = new StackPanel { Spacing = 20 };
        var culture = CultureInfo.GetCultureInfo(L10n.IsFrench ? "fr-FR" : "en-US");
        foreach (var release in releases)
        {
            var section = new StackPanel { Spacing = 6 };
            var heading = release.PublishedAt is { } date
                ? $"{release.Title} · {date.ToLocalTime().ToString("d MMMM yyyy", culture)}"
                : release.Title;
            section.Children.Add(new TextBlock
            {
                Text = heading,
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"],
                TextWrapping = TextWrapping.Wrap,
            });
            section.Children.Add(Render(release.Notes));
            panel.Children.Add(section);
        }
        return new ScrollViewer { Content = panel, MaxHeight = 440, Padding = new Thickness(0, 0, 12, 0) };
    }

    private static RichTextBlock Render(string markdown)
    {
        var text = new RichTextBlock { TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true };
        if (string.IsNullOrWhiteSpace(markdown))
        {
            text.Blocks.Add(Paragraph(L10n.T("No notes for this version.", "Pas de notes pour cette version.")));
            return text;
        }

        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Length == 0 || line.Trim() is "---" or "***") continue;
            if (line.StartsWith("<!--", StringComparison.Ordinal)) continue;

            if (Heading().Match(line) is { Success: true } heading)
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 8, 0, 2) };
                var run = new Run { Text = heading.Groups[2].Value, FontWeight = FontWeights.SemiBold };
                paragraph.Inlines.Add(run);
                text.Blocks.Add(paragraph);
            }
            else if (Bullet().Match(line) is { Success: true } bullet)
            {
                var depth = bullet.Groups[1].Value.Length / 2;
                var paragraph = new Paragraph { Margin = new Thickness(4 + 16 * depth, 2, 0, 2) };
                paragraph.Inlines.Add(new Run { Text = "•  " });
                AddInlines(paragraph, bullet.Groups[2].Value);
                text.Blocks.Add(paragraph);
            }
            else
            {
                var paragraph = new Paragraph { Margin = new Thickness(0, 2, 0, 2) };
                AddInlines(paragraph, line.Trim());
                text.Blocks.Add(paragraph);
            }
        }
        return text;
    }

    private static Paragraph Paragraph(string content)
    {
        var paragraph = new Paragraph();
        paragraph.Inlines.Add(new Run { Text = content });
        return paragraph;
    }

    /// <summary>**bold**, `code`, [text](url) and bare links; everything else as typed.</summary>
    private static void AddInlines(Paragraph paragraph, string line)
    {
        var position = 0;
        foreach (Match match in Inline().Matches(line))
        {
            if (match.Index > position) paragraph.Inlines.Add(new Run { Text = line[position..match.Index] });
            if (match.Groups["bold"].Success)
                paragraph.Inlines.Add(new Run { Text = match.Groups["bold"].Value, FontWeight = FontWeights.SemiBold });
            else if (match.Groups["code"].Success)
                paragraph.Inlines.Add(new Run { Text = match.Groups["code"].Value, FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas") });
            else
                paragraph.Inlines.Add(Link(
                    match.Groups["label"].Success ? match.Groups["label"].Value : ShortUrl(match.Groups["bare"].Value),
                    match.Groups["url"].Success ? match.Groups["url"].Value : match.Groups["bare"].Value));
            position = match.Index + match.Length;
        }
        if (position < line.Length) paragraph.Inlines.Add(new Run { Text = line[position..] });
    }

    private static Inline Link(string label, string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("https" or "http"))
            return new Run { Text = label };
        var link = new Hyperlink { NavigateUri = uri };
        link.Inlines.Add(new Run { Text = label });
        return link;
    }

    /// <summary>".../pull/42" reads as "#42", like on GitHub.</summary>
    private static string ShortUrl(string url) =>
        PullOrIssue().Match(url) is { Success: true } m ? "#" + m.Groups[1].Value : url;

    [GeneratedRegex(@"^(#{1,6})\s+(.+)$")]
    private static partial Regex Heading();

    [GeneratedRegex(@"^(\s*)(?:[-*+]|\d+\.)\s+(.+)$")]
    private static partial Regex Bullet();

    [GeneratedRegex(@"\*\*(?<bold>.+?)\*\*|`(?<code>[^`]+)`|\[(?<label>[^\]]+)\]\((?<url>[^)\s]+)\)|(?<bare>https?://[^\s)]+)")]
    private static partial Regex Inline();

    [GeneratedRegex(@"/(?:pull|issues)/(\d+)$")]
    private static partial Regex PullOrIssue();
}
