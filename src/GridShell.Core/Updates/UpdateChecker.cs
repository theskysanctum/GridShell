using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace GridShell.Core.Updates;

public sealed record UpdateCheckResult(bool UpdateAvailable, string CurrentVersion, string LatestVersion, string ReleaseUrl);

/// <summary>Checks GitHub's own "latest release" API against this build's own assembly version -
/// no telemetry server of our own, just the public releases list every GridShell release already
/// publishes to. Manual only (Settings' "Check for Updates" button) - no background polling, per
/// the user's explicit preference for notify-and-manual over silent auto-update.</summary>
public static class UpdateChecker
{
    private const string ReleasesApiUrl = "https://api.github.com/repos/theskysanctum/gridshell/releases/latest";

    /// <summary>The fixed, version-less asset name every release publishes alongside its
    /// versioned installer (see build-release.yml) - always resolves to whatever's current via
    /// GitHub's own "latest" alias, so this never needs to know the version in advance.</summary>
    public const string DownloadUrl = "https://github.com/theskysanctum/gridshell/releases/latest/download/GridShell-Setup.exe";

    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    static UpdateChecker()
    {
        // The GitHub API rejects requests with no User-Agent header outright.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("GridShell-Updater");
        Http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
    }

    /// <summary>Read from this exe's own assembly version - kept in sync with
    /// installer/GridShell.iss's MyAppVersion by the release workflow's version-bump step, so a
    /// release build's reported version always matches the installer it came from.</summary>
    public static string CurrentVersion =>
        (Assembly.GetEntryAssembly() ?? Assembly.GetExecutingAssembly()).GetName().Version?.ToString(3) ?? "0.0.0";

    public static async Task<UpdateCheckResult> CheckAsync()
    {
        using var response = await Http.GetAsync(ReleasesApiUrl);
        response.EnsureSuccessStatusCode();

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        var tagName = root.TryGetProperty("tag_name", out var tagProp) ? tagProp.GetString() : null;
        var latestVersionText = tagName?.TrimStart('v') ?? CurrentVersion;
        var releaseUrl = root.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;

        var current = Version.Parse(CurrentVersion);
        var updateAvailable = Version.TryParse(latestVersionText, out var latest) && latest > current;

        return new UpdateCheckResult(
            updateAvailable,
            CurrentVersion,
            latestVersionText,
            releaseUrl ?? "https://github.com/theskysanctum/gridshell/releases/latest");
    }

    /// <summary>Streams the installer to <paramref name="destinationPath"/>, reporting 0-100
    /// progress as bytes arrive (GitHub's release-asset redirect always sends a real
    /// Content-Length, so this isn't an indeterminate download in practice).</summary>
    public static async Task DownloadInstallerAsync(string destinationPath, IProgress<double>? progress, CancellationToken ct = default)
    {
        using var response = await Http.GetAsync(DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength;
        await using var contentStream = await response.Content.ReadAsStreamAsync(ct);
        await using var fileStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None);

        var buffer = new byte[81920];
        long totalRead = 0;
        int read;
        while ((read = await contentStream.ReadAsync(buffer, ct)) > 0)
        {
            await fileStream.WriteAsync(buffer.AsMemory(0, read), ct);
            totalRead += read;

            if (totalBytes is > 0)
            {
                progress?.Report(totalRead * 100.0 / totalBytes.Value);
            }
        }
    }
}
