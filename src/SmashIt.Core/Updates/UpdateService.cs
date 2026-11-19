using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace SmashIt.Core.Updates;

/// <summary>A published GitHub release of Smash It.</summary>
public sealed record ReleaseInfo(
    AppVersion Version,
    string Title,
    string Notes,
    DateTimeOffset? PublishedAt,
    string PageUrl,
    string? InstallerUrl,
    string? ChecksumsUrl);

/// <summary>
/// Reads the releases published on GitHub and downloads the installer of a newer one. Nothing is sent but the
/// request itself: no identifier, no statistics.
/// </summary>
public sealed class UpdateService(HttpClient http)
{
    public const string Repository = "geoffreycrofte/smash-it";
    public const string InstallerName = "SmashIt-Setup-x64.exe";
    public const string ChecksumsName = "SHA256SUMS.txt";
    public const string ReleasesPage = $"https://github.com/{Repository}/releases";

    /// <summary>An HTTP client set up for the GitHub API (it requires a User-Agent).</summary>
    public static HttpClient CreateHttpClient(AppVersion current)
    {
        var client = new HttpClient { Timeout = Timeout.InfiniteTimeSpan };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("SmashIt", current.ToString()));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        client.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
        return client;
    }

    /// <summary>The published releases, newest first (drafts are never listed).</summary>
    public async Task<IReadOnlyList<ReleaseInfo>> GetReleasesAsync(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(15));
        var json = await http.GetStringAsync($"https://api.github.com/repos/{Repository}/releases?per_page=30", timeout.Token);
        return ParseReleases(json);
    }

    public static IReadOnlyList<ReleaseInfo> ParseReleases(string json)
    {
        using var document = JsonDocument.Parse(json);
        var releases = new List<ReleaseInfo>();
        foreach (var release in document.RootElement.EnumerateArray())
        {
            if (release.TryGetProperty("draft", out var draft) && draft.GetBoolean()) continue;
            if (!AppVersion.TryParse(Text(release, "tag_name"), out var version)) continue;

            string? installer = null, checksums = null;
            if (release.TryGetProperty("assets", out var assets))
            {
                foreach (var asset in assets.EnumerateArray())
                {
                    var name = Text(asset, "name");
                    var url = Text(asset, "browser_download_url");
                    if (string.Equals(name, InstallerName, StringComparison.OrdinalIgnoreCase)) installer = url;
                    else if (string.Equals(name, ChecksumsName, StringComparison.OrdinalIgnoreCase)) checksums = url;
                }
            }

            DateTimeOffset? published = release.TryGetProperty("published_at", out var date) && date.ValueKind == JsonValueKind.String
                ? date.GetDateTimeOffset()
                : null;
            releases.Add(new ReleaseInfo(
                version,
                Text(release, "name") is { Length: > 0 } title ? title : $"Smash It {version}",
                Text(release, "body") ?? string.Empty,
                published,
                Text(release, "html_url") ?? $"{ReleasesPage}/tag/v{version}",
                installer,
                checksums));
        }
        return releases.OrderByDescending(r => r.Version).ToList();
    }

    private static string? Text(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    /// <summary>
    /// Releases newer than <paramref name="current"/>, newest first. Pre-releases are only offered to someone
    /// already running one.
    /// </summary>
    public static IReadOnlyList<ReleaseInfo> NewerThan(IEnumerable<ReleaseInfo> releases, AppVersion current) =>
        releases.Where(r => r.Version > current && (!r.Version.IsPreRelease || current.IsPreRelease))
            .OrderByDescending(r => r.Version)
            .ToList();

    /// <summary>Releases after <paramref name="previous"/> up to <paramref name="current"/> included: what an update brought.</summary>
    public static IReadOnlyList<ReleaseInfo> Between(IEnumerable<ReleaseInfo> releases, AppVersion previous, AppVersion current) =>
        releases.Where(r => r.Version > previous && r.Version <= current)
            .OrderByDescending(r => r.Version)
            .ToList();

    /// <summary>
    /// What changed after <paramref name="after"/> up to <paramref name="upTo"/> included, newest first, one entry
    /// per version: the sections of CHANGELOG.md as tagged with <paramref name="upTo"/> (so unreleased work on the
    /// main branch never shows), or the GitHub release texts when that tag has no changelog.
    /// </summary>
    /// <param name="language">
    /// Two-letter code of the app's language ("fr"): each version's section is taken from CHANGELOG.fr.md when it
    /// has one, from CHANGELOG.md otherwise. English (null or "en") reads CHANGELOG.md only.
    /// </param>
    public async Task<IReadOnlyList<ReleaseInfo>> GetNotesAsync(
        IReadOnlyList<ReleaseInfo> releases, AppVersion after, AppVersion upTo, CancellationToken cancellationToken,
        string? language = null)
    {
        var fromReleases = Between(releases, after, upTo);
        var english = await FetchChangelogAsync(upTo, Changelog.FileName, cancellationToken);
        var sections = english is null ? [] : Changelog.Between(Changelog.Parse(english), after, upTo);
        if (sections.Count == 0) return fromReleases;

        if (language is { Length: > 0 } and not "en" &&
            await FetchChangelogAsync(upTo, Changelog.TranslatedFileName(language), cancellationToken) is { } translated)
        {
            var bodies = Changelog.Parse(translated).ToDictionary(s => s.Version);
            sections = sections.Select(s => bodies.TryGetValue(s.Version, out var t) ? t : s).ToList();
        }

        return sections.Select(section =>
        {
            var release = releases.FirstOrDefault(r => r.Version.CompareTo(section.Version) == 0);
            return new ReleaseInfo(
                section.Version,
                $"Smash It {section.Version}",
                section.Body,
                release?.PublishedAt,
                release?.PageUrl ?? $"{ReleasesPage}/tag/v{section.Version}",
                release?.InstallerUrl,
                release?.ChecksumsUrl);
        }).ToList();
    }

    /// <summary>A changelog file as tagged with <paramref name="version"/>; <c>null</c> when missing or unreachable.</summary>
    private async Task<string?> FetchChangelogAsync(AppVersion version, string fileName, CancellationToken cancellationToken)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            using var response = await http.GetAsync(
                $"https://raw.githubusercontent.com/{Repository}/v{version}/{fileName}", timeout.Token);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(timeout.Token) : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            return null; // unreachable: the release texts will do
        }
    }

    /// <summary>
    /// Downloads the installer of <paramref name="release"/> into <paramref name="folder"/> and checks it against
    /// the release's SHA256SUMS.txt. A file that does not match is deleted and never run.
    /// </summary>
    public async Task<string> DownloadInstallerAsync(
        ReleaseInfo release, string folder, IProgress<double>? progress, CancellationToken cancellationToken)
    {
        if (release.InstallerUrl is null || release.ChecksumsUrl is null)
            throw new InvalidOperationException("This release has no installer to download.");

        var sums = await http.GetStringAsync(release.ChecksumsUrl, cancellationToken);
        var expected = ChecksumFor(sums, InstallerName)
            ?? throw new InvalidDataException($"{ChecksumsName} does not list {InstallerName}.");

        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, InstallerName);
        using (var response = await http.GetAsync(release.InstallerUrl, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(path);
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total > 0) progress?.Report((double)received / total.Value);
            }
        }

        string actual;
        await using (var file = File.OpenRead(path))
            actual = Convert.ToHexStringLower(await SHA256.HashDataAsync(file, cancellationToken));
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidDataException("The downloaded installer does not match its published checksum.");
        }
        return path;
    }

    /// <summary>The hash listed for <paramref name="fileName"/> in a "hash  name" checksum file, or <c>null</c>.</summary>
    public static string? ChecksumFor(string sums, string fileName)
    {
        foreach (var line in sums.Split('\n'))
        {
            var parts = line.Trim().Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && string.Equals(parts[1].TrimStart('*'), fileName, StringComparison.OrdinalIgnoreCase)
                && parts[0].Length == 64)
                return parts[0].ToLowerInvariant();
        }
        return null;
    }
}
