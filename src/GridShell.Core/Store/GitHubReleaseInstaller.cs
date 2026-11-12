using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace GridShell.Core.Store;

/// <summary>Installs a Store app that has no winget package but does have a real GitHub release
/// with a downloadable Windows build (GithubRepo on its StoreApp entry) - downloads the latest
/// release's best-matching Windows zip via the GitHub REST API and extracts it under
/// %LocalAppData%\GridShell\StoreApps\{id}\, the same way a manual "download and unzip
/// somewhere" install would go, just automated. There's no real installer and nothing registers
/// an Uninstall entry, so "installed" is just "that folder has an exe in it"
/// (InstalledAppLocator.FindBestExeInDirectory), and `/releases/latest` itself already excludes
/// prereleases/dev builds - confirmed against Visual Pinball X's own release history, which
/// tags frequent dev builds as prereleases and keeps genuine stable 10.x releases as the
/// non-prerelease "latest".</summary>
public static class GitHubReleaseInstaller
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GridShell-Store", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static string InstallRoot(string appId) => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GridShell", "StoreApps", appId);

    public static bool IsInstalled(string appId) =>
        InstalledAppLocator.FindBestExeInDirectory(InstallRoot(appId)) is not null;

    public static async Task<InstallResult> InstallAsync(StoreApp app)
    {
        if (string.IsNullOrEmpty(app.GithubRepo))
        {
            return new InstallResult(InstallOutcome.Failed, "No GitHub repo configured for this app.");
        }

        try
        {
            var json = await Http.GetStringAsync($"https://api.github.com/repos/{app.GithubRepo}/releases/latest");
            using var doc = JsonDocument.Parse(json);

            var assets = doc.RootElement.GetProperty("assets").EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
                .ToList();

            var chosen = SelectWindowsZipAsset(assets);
            if (chosen is null)
            {
                return new InstallResult(InstallOutcome.Failed, "No suitable Windows release asset found.");
            }

            var installRoot = InstallRoot(app.Id);
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }

            Directory.CreateDirectory(installRoot);

            var zipPath = Path.Combine(Path.GetTempPath(), $"gridshell-store-{app.Id}-{Guid.NewGuid():N}.zip");
            try
            {
                var bytes = await Http.GetByteArrayAsync(chosen.Value.Url);
                await File.WriteAllBytesAsync(zipPath, bytes);
                ZipFile.ExtractToDirectory(zipPath, installRoot);
            }
            finally
            {
                File.Delete(zipPath);
            }

            return new InstallResult(InstallOutcome.Installed, "Installed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or UnauthorizedAccessException)
        {
            return new InstallResult(InstallOutcome.Failed, ex.Message);
        }
    }

    /// <summary>Picks the best 64-bit, non-debug Windows .zip among a release's assets. Verified
    /// against two real, differently-shaped cases rather than one: Supermodel ships exactly one
    /// Windows zip per release with no arch/config markers at all (passes straight through), and
    /// Visual Pinball X ships 4+ variants per release (Release/Debug x86/x64, and the exact
    /// naming scheme has changed between releases) - excluding "debug"/"x86"/"linux"/"macos"
    /// and preferring a name that still says "win" correctly narrows VPX down to its single
    /// Release x64 Windows zip in both naming schemes seen.</summary>
    private static (string Name, string Url)? SelectWindowsZipAsset(List<(string Name, string Url)> assets)
    {
        var zips = assets.Where(a => a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)).ToList();

        var candidates = zips.Where(a =>
            !a.Name.Contains("debug", StringComparison.OrdinalIgnoreCase) &&
            !a.Name.Contains("linux", StringComparison.OrdinalIgnoreCase) &&
            !a.Name.Contains("macos", StringComparison.OrdinalIgnoreCase) &&
            !a.Name.Contains("x86", StringComparison.OrdinalIgnoreCase)).ToList();

        var preferringWin = candidates.Where(a => a.Name.Contains("win", StringComparison.OrdinalIgnoreCase)).ToList();
        if (preferringWin.Count > 0)
        {
            return preferringWin[0];
        }

        return candidates.Count > 0 ? candidates[0] : null;
    }
}
