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
    /// The game-store launchers (NoLocationWingetIds below) are the one deliberate exception -
    /// confirmed live: Steam's own registry entry breaks (its DisplayIcon/InstallLocation go
    /// missing or wrong) when installed to a custom --location, which is exactly what made the
    /// storefront quick-launch buttons report "Not currently installed" even once actually
    /// installed. Letting those 4 install to their own normal default location keeps that
    /// registry metadata intact for GameLauncherDetector/GameLibraryScanner to find.</summary>
    public const string InstallRoot = @"C:\GridShell\Applications";

    private static readonly HashSet<string> NoLocationWingetIds = new(StringComparer.OrdinalIgnoreCase)
    {
        "Valve.Steam", "EpicGames.EpicGamesLauncher", "GOG.Galaxy", "ItchIo.Itch",
    };

    /// <summary>Whether an app is already installed, per the same `winget list --id X -e` check
    /// InstallAsync uses - lets the Store reflect real install state when it opens, not just
    /// whatever happened during the current app session.</summary>
    public static async Task<bool> IsInstalledAsync(string wingetId)
    {
        var listResult = await RunAsync("list", $"--id {wingetId} -e --accept-source-agreements");
        return listResult is { ExitCode: 0 };
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

        var locationArg = NoLocationWingetIds.Contains(wingetId)
            ? ""
            : $" --location \"{Path.Combine(InstallRoot, app.Id)}\"";
        var installResult = await RunAsync("install",
            $"--id {wingetId} -e --silent --accept-package-agreements --accept-source-agreements " +
            $"--disable-interactivity --source winget{locationArg}");
        if (installResult is null)
        {
            return new InstallResult(InstallOutcome.WingetNotFound,
                "winget (Windows Package Manager / App Installer) wasn't found on this PC.");
        }

        return installResult.Value.ExitCode == 0
            ? new InstallResult(InstallOutcome.Installed, "Installed.")
            : new InstallResult(InstallOutcome.Failed, LastMeaningfulLine(installResult.Value.Output));
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
