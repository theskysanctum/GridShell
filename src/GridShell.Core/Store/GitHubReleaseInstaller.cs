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
/// release's best-matching Windows archive (.zip via .NET's own ZipFile, or .7z via the bundled
/// 7za.exe - RPCS3's only real Windows build is a .7z, confirmed live) via the GitHub REST API
/// and extracts it under WingetInstaller.InstallRoot\{id}\ (C:\GridShell\Applications\{id}\) -
/// the same place winget-installed Store apps land too, where their own installer honors
/// winget's --location flag, so everything GridShell manages stays under one root rather than
/// scattered. The same way a manual "download and unzip somewhere" install would go, just
/// automated. There's no real installer
/// and nothing registers an Uninstall entry, so "installed" is just "that folder has an exe in
/// it" (InstalledAppLocator.FindBestExeInDirectory), and `/releases/latest` itself already
/// excludes prereleases/dev builds - confirmed against Visual Pinball X's own release history,
/// which tags frequent dev builds as prereleases and keeps genuine stable 10.x releases as the
/// non-prerelease "latest".</summary>
public static class GitHubReleaseInstaller
{
    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GridShell-Store", "1.0"));
        // Plain application/json rather than GitHub's own versioned vnd.github+json media type -
        // this client also talks to Forgejo instances (see StoreApp.ApiBaseUrl) exposing the same
        // GitHub-shaped API, which don't recognize GitHub's media type.
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static string InstallRoot(string appId) => Path.Combine(WingetInstaller.InstallRoot, appId);

    public static bool IsInstalled(string appId) =>
        InstalledAppLocator.FindBestExeInDirectory(InstallRoot(appId)) is not null;

    /// <summary>There's no real installer to hand off to, same as InstallAsync's own doc
    /// comment notes - "installed" is just "that folder has an exe in it", so "uninstalled" is
    /// just deleting the folder.</summary>
    public static UninstallResult Uninstall(string appId)
    {
        try
        {
            var installRoot = InstallRoot(appId);
            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }

            return new UninstallResult(UninstallOutcome.Uninstalled, "Uninstalled.");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new UninstallResult(UninstallOutcome.Failed, ex.Message);
        }
    }

    public static async Task<InstallResult> InstallAsync(StoreApp app)
    {
        if (string.IsNullOrEmpty(app.GithubRepo))
        {
            return new InstallResult(InstallOutcome.Failed, "No GitHub repo configured for this app.");
        }

        try
        {
            var json = await Http.GetStringAsync($"{app.ApiBaseUrl}/repos/{app.GithubRepo}/releases/latest");
            using var doc = JsonDocument.Parse(json);

            var assets = doc.RootElement.GetProperty("assets").EnumerateArray()
                .Select(a => (Name: a.GetProperty("name").GetString() ?? "", Url: a.GetProperty("browser_download_url").GetString() ?? ""))
                .ToList();

            var chosen = SelectWindowsAsset(assets);
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

            // A standalone portable exe (confirmed live: Heroic Games Launcher's own release
            // ships "Heroic-<version>-Portable-x64.exe" - a whole self-contained Electron app
            // in one file, no installer and nothing to extract) just gets saved directly into
            // the install root under its own original filename - there's no archive step at
            // all for this case.
            if (chosen.Value.Kind == AssetKind.StandaloneExe)
            {
                var exeBytes = await Http.GetByteArrayAsync(chosen.Value.Url);
                await File.WriteAllBytesAsync(Path.Combine(installRoot, chosen.Value.Name), exeBytes);
                return new InstallResult(InstallOutcome.Installed, "Installed.");
            }

            var archivePath = Path.Combine(Path.GetTempPath(),
                $"gridshell-store-{app.Id}-{Guid.NewGuid():N}{(chosen.Value.Kind == AssetKind.SevenZip ? ".7z" : ".zip")}");
            try
            {
                var bytes = await Http.GetByteArrayAsync(chosen.Value.Url);
                await File.WriteAllBytesAsync(archivePath, bytes);

                if (chosen.Value.Kind == AssetKind.SevenZip)
                {
                    var extractResult = await SevenZipExtractor.ExtractAsync(archivePath, installRoot);
                    if (extractResult is not null)
                    {
                        return new InstallResult(InstallOutcome.Failed, extractResult);
                    }
                }
                else
                {
                    ZipFile.ExtractToDirectory(archivePath, installRoot);
                }
            }
            finally
            {
                File.Delete(archivePath);
            }

            return new InstallResult(InstallOutcome.Installed, "Installed.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException or UnauthorizedAccessException)
        {
            return new InstallResult(InstallOutcome.Failed, ex.Message);
        }
    }

    private enum AssetKind
    {
        Zip,
        SevenZip,
        StandaloneExe,
    }

    /// <summary>Picks the best 64-bit, non-debug Windows asset among a release's assets - an
    /// archive (.zip or .7z) to extract if one exists, otherwise a standalone portable exe
    /// (confirmed live: Heroic Games Launcher ships "Heroic-<version>-Portable-x64.exe" as a
    /// whole self-contained Electron app in one file, alongside a real "-Setup-x64.exe"
    /// installer this deliberately never picks - "portable" in the name is the only thing that
    /// tells the two apart). Verified against several real, differently-shaped cases rather
    /// than one: Supermodel ships exactly one Windows zip per release with no arch/config
    /// markers at all (passes straight through), Visual Pinball X ships 4+ zip variants per
    /// release (Release/Debug x86/x64, and the exact naming scheme has changed between
    /// releases), RPCS3's binaries repo ships a single .7z plus a .sha256 checksum sidecar file
    /// (the extension filter alone excludes the checksum), and Eden ships Windows/Android/
    /// Linux/macOS builds side by side plus several incompatible Windows CPU targets (amd64,
    /// Steam-Deck-style "rog-ally" Zen4-only, and Snapdragon arm64) with standard/PGO compiler-
    /// optimization variants of each - excluding "debug"/"x86"/"linux"/"macos"/"arm64"/
    /// "rog-ally"/"android"/"setup"/"installer", preferring a name that still says "win", and
    /// within that preferring a name saying "pgo" (Eden's own release notes call the PGO build
    /// the recommended one) correctly narrows every case seen down to the one right asset.</summary>
    private static (string Name, string Url, AssetKind Kind)? SelectWindowsAsset(List<(string Name, string Url)> assets)
    {
        bool IsExcluded(string name) =>
            name.Contains("debug", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("linux", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("macos", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("android", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("arm64", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("rog-ally", StringComparison.OrdinalIgnoreCase) ||
            name.Contains("x86", StringComparison.OrdinalIgnoreCase);

        var archiveCandidates = assets.Where(a =>
            (a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
             a.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)) &&
            !IsExcluded(a.Name)).ToList();

        if (archiveCandidates.Count > 0)
        {
            var preferringWin = archiveCandidates.Where(a => a.Name.Contains("win", StringComparison.OrdinalIgnoreCase)).ToList();
            var pool = preferringWin.Count > 0 ? preferringWin : archiveCandidates;
            var preferringPgo = pool.Where(a => a.Name.Contains("pgo", StringComparison.OrdinalIgnoreCase)).ToList();
            var chosen = preferringPgo.Count > 0 ? preferringPgo[0] : pool[0];
            var kind = chosen.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ? AssetKind.SevenZip : AssetKind.Zip;
            return (chosen.Name, chosen.Url, kind);
        }

        var portableExe = assets.FirstOrDefault(a =>
            a.Name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) &&
            a.Name.Contains("portable", StringComparison.OrdinalIgnoreCase) &&
            !IsExcluded(a.Name));
        return portableExe.Name is null ? null : (portableExe.Name, portableExe.Url, AssetKind.StandaloneExe);
    }
}
