using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;

namespace GridShell.Core.Store;

/// <summary>Installs a Store app that has neither a working winget package nor a GitHub release
/// to pull from - Dolphin is the first case: its winget package only ships a stale v5.0 whose
/// download mirror returns 403, and the real dolphin-emu/dolphin repo publishes zero GitHub
/// releases at all, with their own website blocking automated requests outright (confirmed
/// live, all three). The installer archive itself is bundled directly in this repo
/// (store-catalog/packages/{PackageFile}) but fetched over plain HTTP from
/// raw.githubusercontent.com, exactly like StoreSceneSync's scene downloads - deliberately NOT
/// through StoreCatalogSync's app-catalog clone, so opening the Store never pulls down a
/// multi-MB installer archive just to show its Install button; the only download happens when
/// the user actually clicks Install. Extracted to WingetInstaller.InstallRoot\{id}\, same
/// convention as GitHubReleaseInstaller.</summary>
public static class PackageInstaller
{
    private const string RawBaseUrl = "https://raw.githubusercontent.com/theskysanctum/gridshell-store/main/store-catalog/packages/";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GridShell-Store", "1.0"));
        return client;
    }

    public static string InstallRoot(string appId) => Path.Combine(WingetInstaller.InstallRoot, appId);

    public static bool IsInstalled(string appId) =>
        InstalledAppLocator.FindBestExeInDirectory(InstallRoot(appId)) is not null;

    /// <summary>Same as GitHubReleaseInstaller's own Uninstall - no real installer involved,
    /// just a folder GridShell extracted an archive into, so removing it is just deleting that
    /// folder.</summary>
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
        if (string.IsNullOrEmpty(app.PackageFile))
        {
            return new InstallResult(InstallOutcome.Failed, "No package file configured for this app.");
        }

        var installRoot = InstallRoot(app.Id);
        var isSevenZip = app.PackageFile.EndsWith(".7z", StringComparison.OrdinalIgnoreCase);
        var isZip = app.PackageFile.EndsWith(".zip", StringComparison.OrdinalIgnoreCase);
        if (!isSevenZip && !isZip)
        {
            return new InstallResult(InstallOutcome.Failed, $"Unsupported package format: {app.PackageFile}");
        }

        var archivePath = Path.Combine(Path.GetTempPath(),
            $"gridshell-store-{app.Id}-{Guid.NewGuid():N}{(isSevenZip ? ".7z" : ".zip")}");
        try
        {
            var bytes = await Http.GetByteArrayAsync($"{RawBaseUrl}{app.PackageFile}");
            await File.WriteAllBytesAsync(archivePath, bytes);

            if (Directory.Exists(installRoot))
            {
                Directory.Delete(installRoot, recursive: true);
            }

            Directory.CreateDirectory(installRoot);

            if (isSevenZip)
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
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return new InstallResult(InstallOutcome.Failed, ex.Message);
        }
        finally
        {
            File.Delete(archivePath);
        }

        return new InstallResult(InstallOutcome.Installed, "Installed.");
    }
}
