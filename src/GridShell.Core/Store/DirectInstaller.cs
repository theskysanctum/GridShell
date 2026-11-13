using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace GridShell.Core.Store;

/// <summary>Installs Steam, Epic Games Launcher, GOG Galaxy, and itch directly - not through
/// winget, even though winget technically has packages for all 4. Confirmed live, repeatedly:
/// winget's own install-time --location flag breaks these 4 specifically (their own
/// InstallLocation/DisplayIcon go missing or wrong in the registry afterward), and winget's
/// uninstall for a broken entry can fail with a COM error that makes Windows itself pop the
/// default browser open - a problem that goes away entirely by not routing through winget.exe
/// for these 4 at all.
///
/// Still reuses winget-pkgs (microsoft/winget-pkgs on GitHub) as a metadata source, not a
/// runtime dependency - at install time, fetches that package's current manifest (by
/// StoreApp.WingetId, the same winget-pkgs PackageIdentifier already in the catalog) to learn
/// its real installer URL, type, and silent switches, then downloads and runs that installer
/// directly, letting it install to its own real default location exactly like a normal
/// double-click install would - which is what keeps its registry entry correct afterward,
/// unlike a custom --location.</summary>
public static class DirectInstaller
{
    private const string ManifestApiBase = "https://api.github.com/repos/microsoft/winget-pkgs/contents/manifests";
    private const string ManifestRawBase = "https://raw.githubusercontent.com/microsoft/winget-pkgs/master/manifests";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GridShell-Store", "1.0"));
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        return client;
    }

    public static bool IsInstalled(StoreApp app) => InstalledAppLocator.FindMainExe(app.Name) is not null;

    public static async Task<InstallResult> InstallAsync(StoreApp app)
    {
        var manifest = await FetchLatestManifestAsync(app.WingetId);
        if (manifest is null)
        {
            return new InstallResult(InstallOutcome.Failed,
                $"Couldn't read {app.WingetId}'s current installer info from winget-pkgs.");
        }

        // Derived from InstallerType, not the URL itself - several of these URLs carry a query
        // string whose VALUE happens to end in a real-looking extension (confirmed live: GOG's
        // own InstallerUrl is "...download?path=/open/galaxy/client/setup_galaxy_X.exe", whose
        // real path component - download - has no extension at all; only the query value does),
        // so Path.GetExtension on the raw URL can't be trusted here.
        var extension = manifest.InstallerType switch
        {
            "wix" or "msi" => ".msi",
            "zip" => ".zip",
            _ => ".exe",
        };
        var archivePath = Path.Combine(Path.GetTempPath(), $"gridshell-direct-{app.Id}-{Guid.NewGuid():N}{extension}");
        try
        {
            var bytes = await Http.GetByteArrayAsync(manifest.InstallerUrl);
            await File.WriteAllBytesAsync(archivePath, bytes);

            var (exitCode, error) = manifest.InstallerType switch
            {
                "wix" or "msi" => await RunAsync("msiexec.exe", $"/i \"{archivePath}\" {manifest.Silent ?? "/quiet /norestart"}"),
                "zip" => await InstallFromZipAsync(archivePath, manifest),
                "inno" => await RunAsync(archivePath, manifest.Silent ?? "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES"),
                _ => await RunAsync(archivePath, manifest.Silent ?? "/S"),
            };

            // msiexec's own "success, but a reboot is needed to finish" code - a real success,
            // not a failure, for this purpose.
            if (exitCode is 0 or 3010)
            {
                return new InstallResult(InstallOutcome.Installed, "Installed.");
            }

            return new InstallResult(InstallOutcome.Failed, error ?? $"Installer exited with code {exitCode}.");
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or UnauthorizedAccessException)
        {
            return new InstallResult(InstallOutcome.Failed, ex.Message);
        }
        finally
        {
            try
            {
                File.Delete(archivePath);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>itch's own winget manifest packages it as a zip with its real installer
    /// (itch-setup.exe) nested inside - extract, then run that nested exe the same way any
    /// other direct installer gets run.</summary>
    private static async Task<(int ExitCode, string? Error)> InstallFromZipAsync(string zipPath, DirectInstallManifest manifest)
    {
        if (manifest.NestedInstallerRelativePath is null)
        {
            return (-1, "Zip-packaged installer with no nested installer file declared.");
        }

        var extractDir = Path.Combine(Path.GetTempPath(), $"gridshell-direct-extract-{Guid.NewGuid():N}");
        try
        {
            ZipFile.ExtractToDirectory(zipPath, extractDir);
            var nestedPath = Path.Combine(extractDir, manifest.NestedInstallerRelativePath);
            return await RunAsync(nestedPath, manifest.Silent ?? "/S");
        }
        finally
        {
            try
            {
                Directory.Delete(extractDir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    /// <summary>Uninstalls the same way InstallAsync installed - re-fetches the current
    /// manifest purely to know the installer type again (an MSI/wix app is uninstalled via its
    /// own ProductCode, never needing the registry at all; everything else runs its own
    /// registry-declared uninstaller with the matching silent switch). A ghost entry (no real
    /// exe resolves) skips all of that and deletes the orphaned registry entry directly - same
    /// reasoning as WingetInstaller.UninstallAsync's own ghost handling.</summary>
    public static async Task<UninstallResult> UninstallAsync(StoreApp app)
    {
        if (InstalledAppLocator.FindMainExe(app.Name) is null)
        {
            return InstalledAppLocator.TryDeleteOrphanedUninstallEntry(app.Name)
                ? new UninstallResult(UninstallOutcome.Uninstalled, "Uninstalled.")
                : new UninstallResult(UninstallOutcome.Failed,
                    "Couldn't clean up its leftover registry entry - this usually needs admin rights. Try running GridShell as Administrator, or clean it up manually.");
        }

        var manifest = await FetchLatestManifestAsync(app.WingetId);

        if (manifest is { InstallerType: "wix" or "msi", ProductCode: { } productCode })
        {
            var (exitCode, error) = await RunAsync("msiexec.exe", $"/x {productCode} /quiet /norestart");
            return exitCode is 0 or 3010
                ? new UninstallResult(UninstallOutcome.Uninstalled, "Uninstalled.")
                : new UninstallResult(UninstallOutcome.Failed, error ?? $"Uninstaller exited with code {exitCode}.");
        }

        var found = InstalledAppLocator.FindUninstallString(app.Name);
        if (found is not { } uninstall)
        {
            return new UninstallResult(UninstallOutcome.Failed, "Couldn't find its uninstaller.");
        }

        var silentArgs = uninstall.AlreadyQuiet
            ? ""
            : manifest?.InstallerType switch
            {
                "inno" => "/VERYSILENT /NORESTART /SUPPRESSMSGBOXES",
                _ => "/S",
            };

        var (uninstallExitCode, uninstallError) = await RunQuotedCommandAsync(uninstall.Command, silentArgs);
        return uninstallExitCode is 0 or 3010
            ? new UninstallResult(UninstallOutcome.Uninstalled, "Uninstalled.")
            : new UninstallResult(UninstallOutcome.Failed, uninstallError ?? $"Uninstaller exited with code {uninstallExitCode}.");
    }

    private sealed record DirectInstallManifest(
        string InstallerUrl, string InstallerType, string? Silent, string? ProductCode, string? NestedInstallerRelativePath);

    private static async Task<DirectInstallManifest?> FetchLatestManifestAsync(string wingetId)
    {
        try
        {
            var segments = wingetId.Split('.');
            var pathSuffix = string.Join('/', segments);
            var firstLetter = wingetId[..1].ToLowerInvariant();
            var listUrl = $"{ManifestApiBase}/{firstLetter}/{pathSuffix}";

            var listJson = await Http.GetStringAsync(listUrl);
            using var listDoc = System.Text.Json.JsonDocument.Parse(listJson);
            var versions = listDoc.RootElement.EnumerateArray()
                .Select(e => e.GetProperty("name").GetString() ?? "")
                .Where(v => v.Length > 0)
                .ToList();
            if (versions.Count == 0)
            {
                return null;
            }

            var latestVersion = versions
                .OrderBy(v => Version.TryParse(NormalizeForVersionSort(v), out var parsed) ? parsed : new Version(0, 0))
                .ThenBy(v => v, StringComparer.OrdinalIgnoreCase)
                .Last();

            var yamlUrl = $"{ManifestRawBase}/{firstLetter}/{pathSuffix}/{latestVersion}/{wingetId}.installer.yaml";
            var yaml = await Http.GetStringAsync(yamlUrl);

            var installerType = ExtractValue(yaml, "InstallerType") ?? "exe";
            var installerUrl = ExtractValue(yaml, "InstallerUrl");
            if (installerUrl is null)
            {
                return null;
            }

            return new DirectInstallManifest(
                InstallerUrl: installerUrl,
                InstallerType: installerType,
                Silent: ExtractValue(yaml, "Silent"),
                ProductCode: ExtractValue(yaml, "ProductCode"),
                NestedInstallerRelativePath: ExtractValue(yaml, "RelativeFilePath"));
        }
        catch (Exception ex) when (ex is HttpRequestException or System.Text.Json.JsonException)
        {
            return null;
        }
    }

    /// <summary>Version.TryParse needs a dotted numeric string - winget-pkgs version folder
    /// names occasionally carry a trailing letter (confirmed live: GOG Galaxy's own
    /// "2.0.43.71a") that Version.TryParse rejects outright, so this strips anything after the
    /// last digit run in the final segment before parsing.</summary>
    private static string NormalizeForVersionSort(string version) =>
        Regex.Replace(version, @"[^\d.]+$", "");

    private static string? ExtractValue(string yaml, string key)
    {
        // "(?:-\s*)?" tolerates a YAML list-item marker before the key - confirmed live:
        // NestedInstallerFiles' own RelativeFilePath is written as "- RelativeFilePath: ..."
        // (itch's manifest), not just indented, which a plain "^\s*key:" misses entirely.
        var match = Regex.Match(yaml, $@"(?m)^\s*(?:-\s*)?{Regex.Escape(key)}:\s*(.+)$");
        return match.Success ? match.Groups[1].Value.Trim().Trim('\'', '"') : null;
    }

    private static Task<(int ExitCode, string? Error)> RunAsync(string filePath, string arguments) =>
        RunProcessAsync(filePath, arguments);

    /// <summary>The registry's own UninstallString is already a full "program + args" command
    /// line (sometimes quoted, sometimes not) rather than a bare path - split it the same way
    /// CreateProcess itself would before appending our own silent switch.</summary>
    private static Task<(int ExitCode, string? Error)> RunQuotedCommandAsync(string commandLine, string extraArgs)
    {
        string filePath;
        string existingArgs;
        if (commandLine.StartsWith('"'))
        {
            var closingQuote = commandLine.IndexOf('"', 1);
            filePath = commandLine[1..closingQuote];
            existingArgs = commandLine[(closingQuote + 1)..].Trim();
        }
        else
        {
            var spaceIndex = commandLine.IndexOf(' ');
            filePath = spaceIndex < 0 ? commandLine : commandLine[..spaceIndex];
            existingArgs = spaceIndex < 0 ? "" : commandLine[(spaceIndex + 1)..].Trim();
        }

        return RunProcessAsync(filePath, $"{existingArgs} {extraArgs}".Trim());
    }

    private static async Task<(int ExitCode, string? Error)> RunProcessAsync(string filePath, string arguments)
    {
        var startInfo = new ProcessStartInfo(filePath, arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (-1, $"Couldn't start {filePath}.");
            }

            await process.WaitForExitAsync();
            return (process.ExitCode, null);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            return (-1, ex.Message);
        }
    }
}
