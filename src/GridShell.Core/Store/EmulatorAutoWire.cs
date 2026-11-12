using System.IO;
using System.Linq;
using GridShell.Core.Settings;
using GridShell.Core.Tiles;
using Microsoft.Win32;

namespace GridShell.Core.Store;

/// <summary>After WingetInstaller reports an app installed (or already installed), finds its
/// real exe and - if the catalog entry lists platform slugs - fills in Settings'
/// EmulatorConfigs for them, so an emulator installed through the Store is actually usable
/// immediately, not just present on disk. Never overwrites a slug the user already configured
/// themselves.
///
/// Locating the exe reuses GameLauncherDetector's proven approach (scan the Uninstall registry,
/// both hives and both 32/64-bit views, for a DisplayName match, then look under its
/// InstallLocation) but can't assume an exact exe filename the way that one does for well-known
/// launchers - confirmed live on this machine that winget's own "portable (zip)" packages (used
/// by DuckStation/Cemu/Xenia/xemu) still register a normal HKCU Uninstall entry with a real
/// InstallLocation, same as a traditional installer, so the same registry scan covers both
/// kinds. Picks the largest .exe under that folder, skipping typical non-main-exe names
/// (uninstallers, updaters, crash handlers) - a heuristic, not a guarantee, but one that matched
/// reality exactly on xemu's real install (exactly one exe present).</summary>
public static class EmulatorAutoWire
{
    private static readonly string[] SkipNameFragments =
    {
        "uninstall", "unins0", "setup", "updater", "update", "crashpad", "crashhandler",
    };

    public static void TryWire(StoreApp app)
    {
        if (app.PlatformSlugs.Count == 0)
        {
            return;
        }

        var exePath = FindMainExe(app.Name);
        if (exePath is null)
        {
            return;
        }

        var settings = AppSettingsStore.Load();
        var changed = false;

        foreach (var slug in app.PlatformSlugs)
        {
            if (settings.EmulatorConfigs.ContainsKey(slug))
            {
                continue;
            }

            settings.EmulatorConfigs[slug] = new EmulatorConfig { EmulatorPath = exePath };
            changed = true;
        }

        if (changed)
        {
            AppSettingsStore.Save(settings);
        }
    }

    private static string? FindMainExe(string displayNameContains)
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
                    if (string.IsNullOrEmpty(installLocation) || !Directory.Exists(installLocation))
                    {
                        continue;
                    }

                    var best = BestExeIn(installLocation);
                    if (best is not null)
                    {
                        return best;
                    }
                }
            }
        }

        return null;
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
