using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace GridShell.Core.Store;

/// <summary>Finds an installed app's main exe by scanning the Uninstall registry for a
/// DisplayName match - shared by EmulatorAutoWire (to fill in EmulatorConfigs) and the Store UI
/// (to show the app's real icon once installed, instead of only its initial-letter fallback).
/// See EmulatorAutoWire's remarks for how this was verified against real winget installs.</summary>
public static class InstalledAppLocator
{
    private static readonly string[] SkipNameFragments =
    {
        "uninstall", "unins0", "setup", "updater", "update", "crashpad", "crashhandler",
    };

    public static string? FindMainExe(string displayNameContains)
    {
        var hives = new[] { Registry.CurrentUser, Registry.LocalMachine };
        var uninstallKeyPaths = new[]
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall",
        };

        foreach (var hive in hives)
        {
            foreach (var keyPath in uninstallKeyPaths)
            {
                using var uninstallKey = hive.OpenSubKey(keyPath);
                if (uninstallKey is null)
                {
                    continue;
                }

                foreach (var subKeyName in uninstallKey.GetSubKeyNames())
                {
                    using var subKey = uninstallKey.OpenSubKey(subKeyName);
                    var displayName = subKey?.GetValue("DisplayName") as string;
                    if (displayName is null || !displayName.Contains(displayNameContains, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var installLocation = subKey?.GetValue("InstallLocation") as string;
                    if (!string.IsNullOrEmpty(installLocation) && Directory.Exists(installLocation))
                    {
                        var best = BestExeIn(installLocation);
                        if (best is not null)
                        {
                            return best;
                        }
                    }

                    // Some installs (confirmed live: RetroArch's winget package) leave
                    // InstallLocation blank but still point DisplayIcon straight at the main
                    // exe - a real, common enough convention to fall back to rather than
                    // missing the app entirely.
                    if (subKey?.GetValue("DisplayIcon") as string is { } displayIcon
                        && displayIcon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(displayIcon))
                    {
                        return displayIcon;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Same largest-non-skip-pattern heuristic as the registry-driven lookup above, but
    /// against a folder GridShell itself extracted an app into (GitHubReleaseInstaller) rather
    /// than one found via the Uninstall registry - those installs never register there, since
    /// there's no real installer involved, just a zip extraction.</summary>
    public static string? FindBestExeInDirectory(string directory)
    {
        return Directory.Exists(directory) ? BestExeIn(directory) : null;
    }

    private static string? BestExeIn(string installLocation)
    {
        try
        {
            return Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.AllDirectories)
                .Where(path => !SkipNameFragments.Any(fragment =>
                    Path.GetFileName(path).Contains(fragment, StringComparison.OrdinalIgnoreCase)))
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}
