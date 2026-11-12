using System.Diagnostics;
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
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static string InstallRoot(string appId) => Path.Combine(WingetInstaller.InstallRoot, appId);

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

            var chosen = SelectWindowsArchiveAsset(assets);
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

            var isSevenZip = chosen.Value.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);
            var archivePath = Path.Combine(Path.GetTempPath(),
                $"gridshell-store-{app.Id}-{Guid.NewGuid():N}{(isSevenZip ? ".7z" : ".zip")}");
            try
            {
                var bytes = await Http.GetByteArrayAsync(chosen.Value.Url);
                await File.WriteAllBytesAsync(archivePath, bytes);

                if (isSevenZip)
                {
                    var extractResult = await ExtractSevenZipAsync(archivePath, installRoot);
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

    /// <summary>Picks the best 64-bit, non-debug Windows archive (.zip or .7z) among a release's
    /// assets. Verified against three real, differently-shaped cases rather than one: Supermodel
    /// ships exactly one Windows zip per release with no arch/config markers at all (passes
    /// straight through), Visual Pinball X ships 4+ zip variants per release (Release/Debug
    /// x86/x64, and the exact naming scheme has changed between releases), and RPCS3's binaries
    /// repo ships a single .7z plus a .sha256 checksum sidecar file (the extension filter alone
    /// excludes the checksum) - excluding "debug"/"x86"/"linux"/"macos" and preferring a name
    /// that still says "win" correctly narrows every case seen down to the one right asset.</summary>
    private static (string Name, string Url)? SelectWindowsArchiveAsset(List<(string Name, string Url)> assets)
    {
        var archives = assets.Where(a =>
            a.Name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
            a.Name.EndsWith(".7z", StringComparison.OrdinalIgnoreCase)).ToList();

        var candidates = archives.Where(a =>
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

    /// <summary>Shells out to the bundled 7za.exe (7-Zip's official standalone console build,
    /// from ip7z/7zip's own GitHub releases - verified live to run standalone with no companion
    /// DLL, and to extract .7z correctly) since .NET has no built-in .7z support. Same
    /// Tools-folder/AppContext.BaseDirectory convention as every other bundled CLI tool in this
    /// app (chdman, extract-xiso, aria2c - see Aria2Daemon; also now ArchiveOperations' only
    /// archiver). Returns null on success, an error message otherwise.</summary>
    private static async Task<string?> ExtractSevenZipAsync(string archivePath, string destinationDir)
    {
        var toolPath = Path.Combine(AppContext.BaseDirectory, "Tools", "7za.exe");
        if (!File.Exists(toolPath))
        {
            return "7za.exe (bundled 7-Zip extractor) is missing from this install.";
        }

        var startInfo = new ProcessStartInfo(toolPath, $"x \"{archivePath}\" -o\"{destinationDir}\" -y")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return "Couldn't start 7zr.exe.";
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 ? null : (string.IsNullOrWhiteSpace(error) ? output : error);
    }
}
