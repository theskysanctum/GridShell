using System.IO;
using Microsoft.Win32;

namespace GridShell.Core.Interop;

public enum GameLauncher
{
    Steam,
    Epic,
    Gog,
    Itch,
}

/// <summary>Finds each PC game storefront client's own executable, if installed - not game
/// install paths, the launcher app itself (Steam.exe, EpicGamesLauncher.exe, GalaxyClient.exe,
/// itch.exe), so GridShell can offer a quick-launch button for each and (separately) know which
/// exe to close once a game tile that launches through one of them ends.</summary>
public static class GameLauncherDetector
{
    public static string? FindExe(GameLauncher launcher) => launcher switch
    {
        GameLauncher.Steam => FindSteam(),
        GameLauncher.Epic => FindViaUninstallRegistry("Epic Games Launcher", "EpicGamesLauncher.exe"),
        GameLauncher.Gog => FindViaUninstallRegistry("GOG Galaxy", "GalaxyClient.exe"),
        GameLauncher.Itch => FindItch(),
        _ => null,
    };

    /// <summary>HKCU\Software\Valve\Steam\SteamPath - confirmed against the actively-maintained
    /// GameFinder library's own SteamLocationFinder.cs, which reads this exact key/value to get
    /// Steam's install directory (Steam.exe always sits directly in that root).</summary>
    private static string? FindSteam()
    {
        var steamDir = (string?)Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam")?.GetValue("SteamPath");
        if (string.IsNullOrEmpty(steamDir))
        {
            return null;
        }

        var exe = Path.Combine(steamDir.Replace('/', '\\'), "Steam.exe");
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>itch's desktop app is a per-user Squirrel-style install with no system registry
    /// entry - always at this fixed path once installed.</summary>
    private static string? FindItch()
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "itch", "itch.exe");
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>Same technique already used for HidHide's install location - scans the standard
    /// Windows "Installed Programs" uninstall registry (both 64/32-bit views) for a matching
    /// DisplayName, then searches under its InstallLocation for the named exe rather than
    /// assuming an exact subfolder layout.</summary>
    private static string? FindViaUninstallRegistry(string displayNameContains, string exeFileName)
    {
        var hives = new[] { Registry.LocalMachine, Registry.CurrentUser };
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

                    try
                    {
                        var found = Directory.EnumerateFiles(installLocation, exeFileName, SearchOption.AllDirectories).FirstOrDefault();
                        if (found is not null)
                        {
                            return found;
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                }
            }
        }

        return null;
    }
}
