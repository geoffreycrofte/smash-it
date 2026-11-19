using SmashIt.Core.Updates;

namespace SmashIt.Core.Tests;

public class ChangelogTests
{
    private const string Markdown = """
        # Journal des modifications

        Texte d'introduction, ignoré.

        ## Prochaine version

        - Pas encore publié

        ## v0.3.0 · 15 janvier 2027

        - Presets

        ## v0.2.1

        ### Corrections
        - Un bug

        ## [0.2.0] - 2026-12-01

        - Menu clic droit
        - Mises à jour

        ## v0.1.0

        - Première version
        """;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public void Reads_one_section_per_version_and_skips_the_drafts()
    {
        var sections = Changelog.Parse(Markdown);

        Assert.Equal(["0.3.0", "0.2.1", "0.2.0", "0.1.0"], sections.Select(s => s.Version.ToString()));
        Assert.Equal("### Corrections\n- Un bug", sections[1].Body);
        Assert.DoesNotContain(sections, s => s.Body.Contains("Pas encore publié"));
    }

    [Fact]
    public void Keeps_everything_above_the_users_version()
    {
        var sections = Changelog.Parse(Markdown);

        var fromOld = Changelog.Between(sections, AppVersion.Parse("0.1.0"), AppVersion.Parse("0.3.0"));
        var fromRecent = Changelog.Between(sections, AppVersion.Parse("0.2.1"), AppVersion.Parse("0.3.0"));

        Assert.Equal(["0.3.0", "0.2.1", "0.2.0"], fromOld.Select(s => s.Version.ToString()));
        Assert.Equal(["0.3.0"], fromRecent.Select(s => s.Version.ToString()));
    }

    [Fact]
    public void Finds_the_section_of_one_version()
    {
        Assert.Equal("- Menu clic droit\n- Mises à jour", Changelog.SectionFor(Markdown, AppVersion.Parse("0.2.0")));
        Assert.Null(Changelog.SectionFor(Markdown, AppVersion.Parse("0.9.0")));
    }

    [Fact]
    public void Joins_list_items_and_paragraphs_wrapped_in_the_file()
    {
        const string wrapped = """
            ### New
            - **Right-click to compress.** Select images
              in File Explorer.
            - Second item

            A paragraph
            on two lines.
            """;

        Assert.Equal(
            ["### New", "- **Right-click to compress.** Select images in File Explorer.", "- Second item", "A paragraph on two lines."],
            Changelog.LogicalLines(wrapped));
    }

    private static readonly IReadOnlyList<ReleaseInfo> Releases =
    [
        new(AppVersion.Parse("0.3.0"), "Smash It 0.3.0", "texte de la release 0.3.0", new DateTimeOffset(2027, 1, 15, 9, 0, 0, TimeSpan.Zero),
            "https://example.test/v0.3.0", "https://example.test/setup.exe", "https://example.test/sums.txt"),
        new(AppVersion.Parse("0.2.0"), "Smash It 0.2.0", "texte de la release 0.2.0", null, "https://example.test/v0.2.0", null, null),
    ];

    [Fact]
    public async Task Notes_come_from_the_changelog_of_the_new_versions_tag()
    {
        string? requested = null;
        var service = new UpdateService(new HttpClient(new Handler(url => { requested = url; return Markdown; })));

        var notes = await service.GetNotesAsync(Releases, AppVersion.Parse("0.1.0"), AppVersion.Parse("0.3.0"), Ct);

        Assert.Equal("https://raw.githubusercontent.com/geoffreycrofte/smash-it/v0.3.0/CHANGELOG.md", requested);
        Assert.Equal(["0.3.0", "0.2.1", "0.2.0"], notes.Select(n => n.Version.ToString()));
        Assert.Equal("- Presets", notes[0].Notes);
        Assert.Equal(Releases[0].PublishedAt, notes[0].PublishedAt); // date taken from the release
    }

    private const string French = """
        # Journal des modifications

        ## v0.3.0 · 15 janvier 2027

        - Préréglages
        """;

    [Fact]
    public async Task French_notes_come_from_the_translation_and_untranslated_versions_stay_in_english()
    {
        var requested = new List<string>();
        var service = new UpdateService(new HttpClient(new Handler(url =>
        {
            requested.Add(url);
            return url.EndsWith("CHANGELOG.fr.md") ? French : Markdown;
        })));

        var notes = await service.GetNotesAsync(Releases, AppVersion.Parse("0.1.0"), AppVersion.Parse("0.3.0"), Ct, "fr");

        Assert.Contains("https://raw.githubusercontent.com/geoffreycrofte/smash-it/v0.3.0/CHANGELOG.fr.md", requested);
        Assert.Equal(["0.3.0", "0.2.1", "0.2.0"], notes.Select(n => n.Version.ToString()));
        Assert.Equal("- Préréglages", notes[0].Notes);      // translated
        Assert.Equal("### Corrections\n- Un bug", notes[1].Notes); // not translated: English text
    }

    [Fact]
    public async Task English_notes_never_ask_for_a_translation()
    {
        var requested = new List<string>();
        var service = new UpdateService(new HttpClient(new Handler(url => { requested.Add(url); return Markdown; })));

        await service.GetNotesAsync(Releases, AppVersion.Parse("0.1.0"), AppVersion.Parse("0.3.0"), Ct, "en");

        Assert.Single(requested);
    }

    [Fact]
    public async Task Without_a_changelog_the_release_texts_are_used()
    {
        var service = new UpdateService(new HttpClient(new Handler(_ => null)));

        var notes = await service.GetNotesAsync(Releases, AppVersion.Parse("0.1.0"), AppVersion.Parse("0.3.0"), Ct);

        Assert.Equal(["texte de la release 0.3.0", "texte de la release 0.2.0"], notes.Select(n => n.Notes));
    }

    /// <summary>Answers with the given text, or 404 when it is null.</summary>
    private sealed class Handler(Func<string, string?> answer) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(answer(request.RequestUri!.ToString()) is { } text
                ? new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent(text) }
                : new HttpResponseMessage(System.Net.HttpStatusCode.NotFound));
    }
}
