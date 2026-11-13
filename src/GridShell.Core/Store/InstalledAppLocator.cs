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
                        var best = BestExeIn(installLocation, displayName);
                        if (best is not null)
                        {
                            return best;
                        }
                    }

                    // Some installs (confirmed live: RetroArch's winget package) leave
                    // InstallLocation blank but still point DisplayIcon straight at the main
                    // exe - a real, common enough convention to fall back to rather than
                    // missing the app entirely. But not every installer points DisplayIcon at
                    // the main exe - confirmed live: Steam's own registry entry points DisplayIcon
                    // directly at uninstall.exe - so this needs the same skip-fragment filter
                    // BestExeIn already applies, not a blind trust of whatever's there.
                    if (subKey?.GetValue("DisplayIcon") as string is { } displayIcon
                        && displayIcon.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                        && File.Exists(displayIcon)
                        && !IsLikelyNotMainExe(displayIcon))
                    {
                        return displayIcon;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>Same heuristic as the registry-driven lookup above, but against a folder
    /// GridShell itself extracted an app into (GitHubReleaseInstaller) rather than one found via
    /// the Uninstall registry - those installs never register there, since there's no real
    /// installer involved, just a zip extraction. nameHint is optional since not every caller
    /// has a display name handy (e.g. a GitHub-release emulator install already named exactly
    /// after its own StoreApp.Id).</summary>
    public static string? FindBestExeInDirectory(string directory, string? nameHint = null)
    {
        return Directory.Exists(directory) ? BestExeIn(directory, nameHint) : null;
    }

    /// <summary>Picks the real main exe out of an install folder that can contain several -
    /// confirmed live against Brave's own install (which bundles a "BraveVpnWireguardService"
    /// helper exe alongside the real brave.exe, and the helper happens to be the larger file),
    /// where the old "just take the largest non-skip-pattern .exe anywhere under the install
    /// root" heuristic picked the wrong one entirely. Now tries an exact name match first (the
    /// exe's own filename, letters/digits only, case-insensitive, against the same normalization
    /// of nameHint - "GitHubDesktop.exe" vs. "GitHub Desktop" matches this way) before falling
    /// back to the old largest-file heuristic, which still correctly handles installs where the
    /// real exe's name doesn't match the display name at all (e.g. GitHub Desktop's own
    /// Squirrel-style versioned install, where that match already succeeds anyway, or any install
    /// with only one real candidate exe to begin with).</summary>
    private static string? BestExeIn(string installLocation, string? nameHint = null)
    {
        try
        {
            var candidates = Directory.EnumerateFiles(installLocation, "*.exe", SearchOption.AllDirectories)
                .Where(path => !IsLikelyNotMainExe(path))
                .ToList();

            if (nameHint is not null)
            {
                var normalizedHint = NormalizeForNameMatch(nameHint);
                var nameMatch = candidates
                    .Where(path => NormalizeForNameMatch(Path.GetFileNameWithoutExtension(path)) == normalizedHint)
                    .OrderByDescending(path => new FileInfo(path).Length)
                    .FirstOrDefault();

                if (nameMatch is not null)
                {
                    return nameMatch;
                }
            }

            return candidates
                .OrderByDescending(path => new FileInfo(path).Length)
                .FirstOrDefault();
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static string NormalizeForNameMatch(string value) =>
        new(value.Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    private static bool IsLikelyNotMainExe(string path) =>
        SkipNameFragments.Any(fragment => Path.GetFileName(path).Contains(fragment, StringComparison.OrdinalIgnoreCase));
}
