using System.Diagnostics;
using System.Reflection;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.UI.Xaml.Controls;
using SmashIt.App.Services;
using SmashIt.Core.Updates;

namespace SmashIt.App.ViewModels;

/// <summary>
/// Looks for a newer release when the app starts (GitHub releases), shows it in a banner with its notes, and
/// installs it: the installer is downloaded, checked against the release's SHA-256, then run silently; it starts
/// the app again once done. The portable build only links to the download page.
/// </summary>
public sealed partial class UpdateViewModel(Func<bool> isBusy) : ObservableObject
{
    /// <summary>The running version (from the build: "0.2.0", "0.3.0-beta.1").</summary>
    public static AppVersion Current { get; } = ReadCurrentVersion();

    /// <summary>Installed with the installer (its uninstaller sits next to the app), not run from the zip.</summary>
    public static bool IsInstalled { get; } = File.Exists(Path.Combine(AppContext.BaseDirectory, "unins000.exe"));

    /// <summary>The language of the release notes: the app's own (CHANGELOG.fr.md in French).</summary>
    private static string NotesLanguage => L10n.IsFrench ? "fr" : "en";

    private static readonly string DownloadFolder = Path.Combine(Path.GetTempPath(), "SmashIt-update");

    private static readonly Lazy<UpdateService> Service = new(() => new UpdateService(UpdateService.CreateHttpClient(Current)));

    private static AppVersion ReadCurrentVersion()
    {
        var assembly = typeof(UpdateViewModel).Assembly;
        var text = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString();
        return AppVersion.TryParse(text, out var version) ? version : new AppVersion(0, 0, 0, null);
    }

    [ObservableProperty]
    public partial bool CheckAtStartup { get; set; } = AppSettings.Current.CheckForUpdates;

    partial void OnCheckAtStartupChanged(bool value)
    {
        AppSettings.Current.CheckForUpdates = value;
        AppSettings.Current.Save();
    }

    // The banner above the file list.
    [ObservableProperty]
    public partial bool IsBannerOpen { get; set; }

    [ObservableProperty]
    public partial string BannerTitle { get; set; } = string.Empty;

    [ObservableProperty]
    public partial string BannerMessage { get; set; } = string.Empty;

    [ObservableProperty]
    public partial InfoBarSeverity BannerSeverity { get; set; } = InfoBarSeverity.Informational;

    /// <summary>A newer version is offered (the install and skip buttons show).</summary>
    [ObservableProperty]
    public partial bool IsUpdateOffered { get; set; }

    [ObservableProperty]
    public partial bool HasNotes { get; set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsIdle))]
    public partial bool IsDownloading { get; set; }

    public bool IsIdle => !IsDownloading;

    [ObservableProperty]
    public partial double DownloadProgress { get; set; }

    /// <summary>The result of "Check now", under the button in Settings.</summary>
    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    /// <summary>"Update" for an installed app, "Download" for the portable one.</summary>
    public static string InstallLabel => IsInstalled ? L10n.T("Update", "Mettre à jour") : L10n.T("Download", "Télécharger");

    /// <summary>Shows release notes (newest first). Set by the window.</summary>
    public Func<string, IReadOnlyList<ReleaseInfo>, Task>? ShowNotes { get; set; }

    /// <summary>Raised once the installer is running: the app must close so its files can be replaced.</summary>
    public event Action? ExitRequested;

    private IReadOnlyList<ReleaseInfo> _notes = [];
    private ReleaseInfo? _offered;
    private string _notesTitle = string.Empty;

    /// <summary>At startup: "just updated" news, then a quiet look for a newer version.</summary>
    public async Task StartupAsync()
    {
        var settings = AppSettings.Current;
        AppVersion? previous = AppVersion.TryParse(settings.LastRunVersion, out var last) ? last : null;
        if (settings.LastRunVersion != Current.ToString())
        {
            settings.LastRunVersion = Current.ToString();
            settings.Save();
        }
        var justUpdated = previous is not null && Current > previous;
        try
        {
            // The installer of the last update, no longer needed (still running for a moment after the relaunch).
            if (Directory.Exists(DownloadFolder)) Directory.Delete(DownloadFolder, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }

        if (!CheckAtStartup)
        {
            if (justUpdated) ShowUpdated([]);
            return;
        }

        // Let the window settle first: the check is never what the user opened the app for.
        await Task.Delay(TimeSpan.FromSeconds(2));
        IReadOnlyList<ReleaseInfo> releases;
        try
        {
            releases = await Service.Value.GetReleasesAsync(CancellationToken.None);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or InvalidOperationException)
        {
            // Offline or GitHub unreachable: say nothing, try again next time.
            if (justUpdated) ShowUpdated([]);
            return;
        }

        var newer = UpdateService.NewerThan(releases, Current);
        if (newer.Count > 0 && newer[0].Version.ToString() != settings.SkippedVersion)
            Offer(newer[0], await Service.Value.GetNotesAsync(releases, Current, newer[0].Version, CancellationToken.None, NotesLanguage));
        else if (justUpdated)
            ShowUpdated(await Service.Value.GetNotesAsync(releases, previous!, Current, CancellationToken.None, NotesLanguage));
    }

    [RelayCommand]
    private async Task CheckNowAsync()
    {
        Status = L10n.T("Checking…", "Recherche en cours…");
        try
        {
            var releases = await Service.Value.GetReleasesAsync(CancellationToken.None);
            var newer = UpdateService.NewerThan(releases, Current);
            if (newer.Count == 0)
            {
                Status = L10n.T($"Smash It {Current} is up to date.", $"Smash It {Current} est à jour.");
                return;
            }
            Status = L10n.T($"Smash It {newer[0].Version} is available.", $"Smash It {newer[0].Version} est disponible.");
            // Even a skipped version: the user asked.
            Offer(newer[0], await Service.Value.GetNotesAsync(releases, Current, newer[0].Version, CancellationToken.None, NotesLanguage));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException
                                       or InvalidOperationException)
        {
            Status = L10n.T(
                "Can't reach GitHub right now. Check your connection and try again.",
                "Impossible de joindre GitHub pour l'instant. Vérifiez la connexion et réessayez.");
        }
    }

    /// <param name="notes">Every version after the one running, newest first (CHANGELOG.md, or the release texts).</param>
    private void Offer(ReleaseInfo latest, IReadOnlyList<ReleaseInfo> notes)
    {
        _offered = latest;
        _notes = notes;
        _notesTitle = L10n.T("What's new", "Nouveautés");
        BannerSeverity = InfoBarSeverity.Informational;
        BannerTitle = L10n.T($"Smash It {_offered.Version} is available", $"Smash It {_offered.Version} est disponible");
        BannerMessage = IsInstalled
            ? L10n.T($"You have version {Current}. The update takes a few seconds and keeps your settings.",
                     $"Vous avez la version {Current}. La mise à jour prend quelques secondes et conserve vos réglages.")
            : L10n.T($"You have version {Current}. Download the new one and replace this folder.",
                     $"Vous avez la version {Current}. Téléchargez la nouvelle et remplacez ce dossier.");
        HasNotes = notes.Any(r => !string.IsNullOrWhiteSpace(r.Notes));
        IsUpdateOffered = true;
        IsBannerOpen = true;
    }

    private void ShowUpdated(IReadOnlyList<ReleaseInfo> brought)
    {
        _offered = null;
        _notes = brought;
        _notesTitle = L10n.T($"New in Smash It {Current}", $"Nouveautés de Smash It {Current}");
        BannerSeverity = InfoBarSeverity.Success;
        BannerTitle = L10n.T($"Smash It was updated to {Current}", $"Smash It a été mis à jour en {Current}");
        BannerMessage = string.Empty;
        // Without the notes (offline, checks turned off), the button opens the release page.
        HasNotes = true;
        IsUpdateOffered = false;
        IsBannerOpen = true;
    }

    [RelayCommand]
    private async Task ShowNotesAsync()
    {
        if (_notes.Any(r => !string.IsNullOrWhiteSpace(r.Notes)) && ShowNotes is { } show)
            await show(_notesTitle, _notes);
        else
            OpenInBrowser($"{UpdateService.ReleasesPage}/tag/v{Current}");
    }

    [RelayCommand]
    private void Skip()
    {
        if (_offered is not null)
        {
            AppSettings.Current.SkippedVersion = _offered.Version.ToString();
            AppSettings.Current.Save();
        }
        IsBannerOpen = false;
    }

    [RelayCommand]
    private async Task InstallAsync()
    {
        if (_offered is not { } release) return;
        if (!IsInstalled || release.InstallerUrl is null || release.ChecksumsUrl is null)
        {
            OpenInBrowser(release.PageUrl);
            return;
        }
        if (isBusy())
        {
            BannerMessage = L10n.T(
                "Files are still being compressed: update once they are done (or stop them).",
                "Des fichiers sont encore en cours de compression : mettez à jour une fois terminé (ou arrêtez-les).");
            return;
        }

        IsDownloading = true;
        DownloadProgress = 0;
        BannerMessage = L10n.T("Downloading the update…", "Téléchargement de la mise à jour…");
        try
        {
            var progress = new Progress<double>(value => DownloadProgress = value * 100);
            var installer = await Service.Value.DownloadInstallerAsync(release, DownloadFolder, progress, CancellationToken.None);

            // Silent: only a progress window. /RELAUNCH=1 has the installer start Smash It again when done; it
            // keeps the install mode (current user or all users) and the options chosen the first time.
            Process.Start(new ProcessStartInfo(installer, "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1")
            {
                UseShellExecute = true,
            });
            ExitRequested?.Invoke();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException
                                       or InvalidDataException or InvalidOperationException
                                       or System.ComponentModel.Win32Exception)
        {
            IsDownloading = false;
            BannerSeverity = InfoBarSeverity.Error;
            BannerMessage = ex is System.ComponentModel.Win32Exception
                ? L10n.T("The update was cancelled.", "La mise à jour a été annulée.") // UAC prompt declined
                : L10n.T($"The update failed: {ex.Message}", $"La mise à jour a échoué : {ex.Message}");
        }
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // No browser: nothing more to do.
        }
    }
}
