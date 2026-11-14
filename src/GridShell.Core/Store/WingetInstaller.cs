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
    /// location instead, same as it always did, not a failure either way.
    ///
    /// Steam is the one exception - StoreApp.RequiresElevation skips --location for it (a
    /// custom location broke its own registry entry regardless of elevation) and routes its
    /// install/uninstall through RunElevatedAsync instead.</summary>
    public const string InstallRoot = @"C:\GridShell\Applications";

    /// <summary>Whether an app is already installed, per the same `winget list --id X -e` check
    /// InstallAsync uses - lets the Store reflect real install state when it opens, not just
    /// whatever happened during the current app session.
    ///
    /// winget's own "installed" answer can be stale on its own, independent of the Windows
    /// registry entirely - confirmed live: after manually deleting an app's registry entry,
    /// `winget list` kept reporting it installed anyway, traced to winget's own local
    /// correlation cache (LocalState\Microsoft.Winget.Source_8wekyb3d8bbwe\installed.db) still
    /// holding a stale match by ProductCode, which `winget source update` doesn't refresh and
    /// which survives even deleting that cache file outright. Cross-checking against
    /// InstalledAppLocator - the same real registry/exe lookup the rest of the Store already
    /// trusts - catches that: winget saying yes isn't believed unless a real exe actually
    /// resolves too.</summary>
    public static async Task<bool> IsInstalledAsync(StoreApp app)
    {
        var listResult = await RunAsync("list", $"--id {app.WingetId} -e --accept-source-agreements");
        if (listResult is not { ExitCode: 0 })
        {
            return false;
        }

        return InstalledAppLocator.FindMainExe(app.Name) is not null;
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

        // Same stale-correlation-cache check IsInstalledAsync already applies - winget's own
        // local cache (installed.db) can keep reporting a match here even after the app was
        // fully removed outside GridShell (confirmed live: Telegram, no registry entry or
        // install folder left, winget list still returns it). Trusting listResult alone would
        // wrongly block a real reinstall with "Already installed."
        if (listResult.Value.ExitCode == 0 && InstalledAppLocator.FindMainExe(app.Name) is not null)
        {
            return new InstallResult(InstallOutcome.AlreadyInstalled, "Already installed.");
        }

        var locationArg = app.RequiresElevation ? "" : $" --location \"{Path.Combine(InstallRoot, app.Id)}\"";
        var installArgs = $"--id {wingetId} -e --silent --accept-package-agreements --accept-source-agreements " +
                           $"--disable-interactivity --source winget{locationArg}";

        if (app.RequiresElevation)
        {
            var elevatedExitCode = await RunElevatedAsync("install", installArgs);
            return elevatedExitCode is 0
                ? new InstallResult(InstallOutcome.Installed, "Installed.")
                : new InstallResult(InstallOutcome.Failed,
                    "Install failed, or the admin prompt was cancelled.");
        }

        var installResult = await RunAsync("install", installArgs);
        if (installResult is null)
        {
            return new InstallResult(InstallOutcome.WingetNotFound,
                "winget (Windows Package Manager / App Installer) wasn't found on this PC.");
        }

        return installResult.Value.ExitCode == 0
            ? new InstallResult(InstallOutcome.Installed, "Installed.")
            : new InstallResult(InstallOutcome.Failed, LastMeaningfulLine(installResult.Value.Output));
    }

    /// <summary>Checks InstalledAppLocator first, same as IsInstalledAsync - if no real exe
    /// resolves, this is a ghost entry (deleted outside GridShell between the Store last
    /// refreshing and this click), and winget's own uninstall has nothing real to call: trying
    /// anyway was confirmed live to fail with a COM error (0x800401f5, "Application not
    /// found") that makes Windows itself pop the default browser open with a "search the web
    /// for this app" prompt - a window GridShell, as a shell replacement, can never let
    /// through. A ghost gets its Uninstall registry entry deleted directly instead - no UI of
    /// its own even when denied (a machine-wide entry needs admin rights GridShell doesn't run
    /// with).
    ///
    /// For a real install, shells out to `winget uninstall`, then best-effort deletes whatever
    /// --location folder InstallAsync used - winget's own uninstaller doesn't reliably clean
    /// that up for every installer technology.</summary>
    public static async Task<UninstallResult> UninstallAsync(StoreApp app)
    {
        if (InstalledAppLocator.FindMainExe(app.Name) is null)
        {
            return InstalledAppLocator.TryDeleteOrphanedUninstallEntry(app.Name)
                ? new UninstallResult(UninstallOutcome.Uninstalled, "Uninstalled.")
                : new UninstallResult(UninstallOutcome.Failed,
                    "Couldn't clean up its leftover registry entry - this usually needs admin rights. Try running GridShell as Administrator, or clean it up manually.");
        }

        var uninstallArgs = $"--id {app.WingetId} -e --silent --accept-source-agreements --disable-interactivity";

        if (app.RequiresElevation)
        {
            var elevatedExitCode = await RunElevatedAsync("uninstall", uninstallArgs);
            if (elevatedExitCode != 0)
            {
                return new UninstallResult(UninstallOutcome.Failed, "Uninstall failed, or the admin prompt was cancelled.");
            }
        }
        else
        {
            var result = await RunAsync("uninstall", uninstallArgs);
            if (result is null)
            {
                return new UninstallResult(UninstallOutcome.WingetNotFound,
                    "winget (Windows Package Manager / App Installer) wasn't found on this PC.");
            }

            if (result.Value.ExitCode != 0)
            {
                return new UninstallResult(UninstallOutcome.Failed, LastMeaningfulLine(result.Value.Output));
            }
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

    /// <summary>Runs winget.exe elevated via Windows' own "runas" verb - a single UAC prompt
    /// for just this one winget.exe invocation, not GridShell itself running elevated (which
    /// would also elevate every app/game TileLauncher launches, inherited from the parent
    /// process - never wanted that). UseShellExecute=true is required for "runas" to trigger
    /// the consent prompt at all, which means no output redirection is possible here (a real
    /// Windows constraint, not a choice) - only the exit code comes back, so callers get a
    /// generic failure message rather than winget's own last line of output.</summary>
    private static async Task<int?> RunElevatedAsync(string command, string arguments)
    {
        var startInfo = new ProcessStartInfo("winget.exe", $"{command} {arguments}")
        {
            UseShellExecute = true,
            Verb = "runas",
        };

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return null;
            }

            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            // Covers both "winget.exe genuinely isn't on PATH" and "the user clicked No on the
            // UAC prompt" - Windows surfaces both as the same exception here, with no reliable
            // way to tell them apart from the exception alone.
            return null;
        }
    }

    private static string LastMeaningfulLine(string output)
    {
        var lines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return lines.Length == 0 ? "winget reported a failure with no further detail." : lines[^1];
    }
}
