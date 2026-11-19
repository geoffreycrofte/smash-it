using SmashIt.Core.Updates;

namespace SmashIt.Core.Tests;

public class UpdateServiceTests
{
    [Theory]
    [InlineData("0.2.0", "0.1.0")]
    [InlineData("1.0.0", "0.9.9")]
    [InlineData("0.10.0", "0.9.0")]
    [InlineData("0.2.0", "0.2.0-beta.3")]
    [InlineData("0.2.0-beta.10", "0.2.0-beta.2")]
    [InlineData("0.2.0-rc.1", "0.2.0-beta.5")]
    public void Compares_versions(string newer, string older)
    {
        Assert.True(AppVersion.Parse(newer) > AppVersion.Parse(older));
        Assert.True(AppVersion.Parse(older) < AppVersion.Parse(newer));
    }

    [Fact]
    public void Reads_tags_and_assembly_versions()
    {
        Assert.Equal(new AppVersion(0, 2, 0, null), AppVersion.Parse("v0.2.0"));
        Assert.Equal(new AppVersion(0, 2, 0, null), AppVersion.Parse("0.2.0+4f2a9c1")); // InformationalVersion
        Assert.Equal(new AppVersion(1, 0, 0, "beta.1"), AppVersion.Parse("1.0.0-beta.1"));
        Assert.Equal(new AppVersion(0, 1, 0, null), AppVersion.Parse("0.1.0.0"));
        Assert.False(AppVersion.TryParse("latest", out _));
        Assert.False(AppVersion.TryParse("1.2.3-", out _));
    }

    private const string Json = """
        [
          { "tag_name": "v0.3.0", "name": "Smash It 0.3.0", "draft": true, "body": "brouillon", "assets": [] },
          { "tag_name": "v0.3.0-beta.1", "name": "", "draft": false, "prerelease": true, "body": "bêta",
            "html_url": "https://github.com/geoffreycrofte/smash-it/releases/tag/v0.3.0-beta.1", "assets": [] },
          { "tag_name": "v0.2.0", "name": "Smash It 0.2.0", "draft": false, "body": "## Nouveautés\n- Menu clic droit",
            "published_at": "2026-12-01T10:00:00Z",
            "html_url": "https://github.com/geoffreycrofte/smash-it/releases/tag/v0.2.0",
            "assets": [
              { "name": "SmashIt-Setup-x64.exe", "browser_download_url": "https://example.test/setup.exe" },
              { "name": "SmashIt-win-x64.zip", "browser_download_url": "https://example.test/app.zip" },
              { "name": "SHA256SUMS.txt", "browser_download_url": "https://example.test/sums.txt" }
            ] },
          { "tag_name": "v0.1.0", "name": "Smash It 0.1.0", "draft": false, "body": "Première version", "assets": [] }
        ]
        """;

    [Fact]
    public void Reads_the_releases_and_skips_drafts()
    {
        var releases = UpdateService.ParseReleases(Json);

        Assert.Equal(["0.3.0-beta.1", "0.2.0", "0.1.0"], releases.Select(r => r.Version.ToString()));
        var release = releases[1];
        Assert.Equal("Smash It 0.2.0", release.Title);
        Assert.Equal("https://example.test/setup.exe", release.InstallerUrl);
        Assert.Equal("https://example.test/sums.txt", release.ChecksumsUrl);
        Assert.Equal(new DateTimeOffset(2026, 12, 1, 10, 0, 0, TimeSpan.Zero), release.PublishedAt);
        Assert.Equal("Smash It 0.3.0-beta.1", releases[0].Title); // empty name: a title is made up
    }

    [Fact]
    public void Offers_pre_releases_only_to_people_running_one()
    {
        var releases = UpdateService.ParseReleases(Json);

        Assert.Equal(["0.2.0"], UpdateService.NewerThan(releases, AppVersion.Parse("0.1.0")).Select(r => r.Version.ToString()));
        Assert.Empty(UpdateService.NewerThan(releases, AppVersion.Parse("0.2.0")));
        Assert.Equal(["0.3.0-beta.1"],
            UpdateService.NewerThan(releases, AppVersion.Parse("0.2.1-beta.1")).Select(r => r.Version.ToString()));
    }

    [Fact]
    public void Lists_what_an_update_brought()
    {
        var releases = UpdateService.ParseReleases(Json);

        var brought = UpdateService.Between(releases, AppVersion.Parse("0.1.0"), AppVersion.Parse("0.2.0"));

        Assert.Equal(["0.2.0"], brought.Select(r => r.Version.ToString()));
    }

    [Fact]
    public void Finds_the_checksum_of_a_file()
    {
        var hash = new string('a', 64);
        var sums = $"{new string('b', 64)}  SmashIt-win-x64.zip\r\n{hash}  SmashIt-Setup-x64.exe\r\n";

        Assert.Equal(hash, UpdateService.ChecksumFor(sums, "SmashIt-Setup-x64.exe"));
        Assert.Equal(hash, UpdateService.ChecksumFor($"{hash} *SmashIt-Setup-x64.exe", "SmashIt-Setup-x64.exe"));
        Assert.Null(UpdateService.ChecksumFor(sums, "other.exe"));
    }

    [Fact]
    public async Task Refuses_an_installer_that_does_not_match_its_checksum()
    {
        using var dir = new TempDirectory();
        var installer = "fake installer"u8.ToArray();
        var good = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(installer));
        var release = new ReleaseInfo(AppVersion.Parse("0.2.0"), "Smash It 0.2.0", "", null, "https://example.test/page",
            "https://example.test/setup.exe", "https://example.test/sums.txt");

        var ok = new UpdateService(new HttpClient(new FakeHandler(installer, $"{good}  SmashIt-Setup-x64.exe")));
        var path = await ok.DownloadInstallerAsync(release, dir.Path, null, TestContext.Current.CancellationToken);
        Assert.Equal(installer, await File.ReadAllBytesAsync(path, TestContext.Current.CancellationToken));

        var tampered = new UpdateService(new HttpClient(new FakeHandler(installer, $"{new string('0', 64)}  SmashIt-Setup-x64.exe")));
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            tampered.DownloadInstallerAsync(release, dir.Path, null, TestContext.Current.CancellationToken));
        Assert.False(File.Exists(Path.Combine(dir.Path, UpdateService.InstallerName)));
    }

    private sealed class FakeHandler(byte[] installer, string sums) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = request.RequestUri!.AbsolutePath.EndsWith(".txt")
                    ? new StringContent(sums)
                    : new ByteArrayContent(installer),
            });
    }
}
