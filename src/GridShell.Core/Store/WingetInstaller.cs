using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GridShell.Core.Store;

public enum InstallOutcome
{
    Installed,
    AlreadyInstalled,
    Failed,
    WingetNotFound,
}

public sealed record InstallResult(InstallOutcome Outcome, string Message);

public enum UninstallOutcome
{
    Uninstalled,
    Failed,
    WingetNotFound,
}

public sealed record UninstallResult(UninstallOutcome Outcome, string Message);

/// <summary>Shells out to winget.exe (Windows Package Manager) rather than any COM/WinRT API -
/// confirmed on a real machine (this one): `winget list --id &lt;id&gt; -e` exits 0 when already
/// installed and 20 when not (undocumented by Microsoft; both empirically verified here, not
/// guessed), and `winget install` exits 0 on a fresh successful install. Checking "already
/// installed" via `list` first avoids relying on install's own less predictable exit codes for
/// that case (verified separately: installing an already-up-to-date package exits 43 with
/// "No newer package versions are available", not 0).</summary>
public static class WingetInstaller
{
    /// <summary>Every GridShell-managed app lives under here, so installs stay out of the usual
    /// scattered Program Files/per-package locations - confirmed live against 3 real packages
    /// spanning both installer technologies winget uses (7zip.7zip and Audacity.Audacity's .msi
    /// installers, OBSProject.OBSStudio's .exe one), all 3 honoring winget's own `--location`
    /// flag and landing exactly here instead of their normal default. Winget's own docs mark
    /// --location as "if supported" per-package, so this isn't guaranteed for every catalog
    /// entry - an install that doesn't support it just lands in that installer's own default
    /// location instead, same as it always did, not a failure either way.</summary>
    public const string InstallRoot = @"C:\GridShell\Applications";

    /// <summary>Whether an app is already installed, per the same `winget list --id X -e` check
    /// InstallAsync uses - lets the Store reflect real install state when it opens, not just
    /// whatever happened during the current app session.
    ///
    /// winget's own package database can go stale if the app's files are deleted outside
    /// GridShell entirely, with no uninstall ever run - confirmed live: a user deleting
    /// C:\GridShell\Applications by hand left every winget-tracked app still "installed" per
    /// `winget list`, so the Store kept showing Uninstall (not Install) for apps whose exe no
    /// longer existed, and the quick-launch buttons that depend on the same winget-reported
    /// state silently did nothing. Cross-checking against InstalledAppLocator - the same real
    /// exe lookup the rest of the Store already trusts - catches that: a "ghost" entry (winget
    /// says yes, no exe actually resolves) gets a best-effort winget uninstall to clear the
    /// stale registration, then reports NotInstalled either way, so the Store self-heals back
    /// to a working Install button instead of staying stuck.</summary>
    public static async Task<bool> IsInstalledAsync(StoreApp app)
    {
        var listResult = await RunAsync("list", $"--id {app.WingetId} -e --accept-source-agreements");
        if (listResult is not { ExitCode: 0 })
        {
            return false;
        }

        if (InstalledAppLocator.FindMainExe(app.Name) is not null)
        {
            return true;
        }

        await UninstallAsync(app);
        return false;
    }

    public static async Task<InstallResult> InstallAsync(StoreApp app)
    {
        var wingetId = app.WingetId;
        var listResult = await RunAsync("list", $"--id {wingetId} -e --accept-source-agreements");
        if (listResult is null)
        {
            return new InstallResult(InstallOutcome.WingetNotFound,
                "winget (Windows Package Manager / App Installer) wasn't found on this PC.");
        }

        if (listResult.Value.ExitCode == 0)
        {
            return new InstallResult(InstallOutcome.AlreadyInstalled, "Already installed.");
        }

        var location = Path.Combine(InstallRoot, app.Id);
        var installResult = await RunAsync("install",
            $"--id {wingetId} -e --silent --accept-package-agreements --accept-source-agreements " +
            $"--disable-interactivity --source winget --location \"{location}\"");
        if (installResult is null)
        {
            return new InstallResult(InstallOutcome.WingetNotFound,
                "winget (Windows Package Manager / App Installer) wasn't found on this PC.");
        }

        return installResult.Value.ExitCode == 0
            ? new InstallResult(InstallOutcome.Installed, "Installed.")
            : new InstallResult(InstallOutcome.Failed, LastMeaningfulLine(installResult.Value.Output));
    }

    /// <summary>Shells out to `winget uninstall`, then best-effort deletes whatever's left under
    /// this app's --location folder (InstallAsync's own location) - winget's own uninstaller
    /// doesn't reliably clean that up for every installer technology, same "harmless clutter,
    /// don't fail over it" tolerance StoreCatalogSync already uses for its own stale-clone
    /// cleanup.</summary>
    public static async Task<UninstallResult> UninstallAsync(StoreApp app)
    {
        var result = await RunAsync("uninstall",
            $"--id {app.WingetId} -e --silent --accept-source-agreements --disable-interactivity");
        if (result is null)
        {
            return new UninstallResult(UninstallOutcome.WingetNotFound,
                "winget (Windows Package Manager / App Installer) wasn't found on this PC.");
        }

        if (result.Value.ExitCode != 0)
        {
            return new UninstallResult(UninstallOutcome.Failed, LastMeaningfulLine(result.Value.Output));
        }

        var location = Path.Combine(InstallRoot, app.Id);
        if (Directory.Exists(location))
        {
            try
            {
                Directory.Delete(location, recursive: true);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }

        return new UninstallResult(UninstallOutcome.Uninstalled, "Uninstalled.");
    }

    private static async Task<(int ExitCode, string Output)?> RunAsync(string command, string arguments)
    {
        var startInfo = new ProcessStartInfo("winget.exe", $"{command} {arguments}")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            return (process.ExitCode, string.IsNullOrWhiteSpace(output) ? error : output);
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // winget.exe genuinely isn't on PATH/installed - a real, documented possibility
            // (App Installer isn't guaranteed present), not just a theoretical edge case.
            return null;
        }
    }

    private static string LastMeaningfulLine(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "winget reported a failure with no further detail." : lines[^1];
    }
}
