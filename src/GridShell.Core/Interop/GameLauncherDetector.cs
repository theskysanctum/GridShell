using System.IO;
using GridShell.Core.Store;
using Microsoft.Win32;

namespace GridShell.Core.Interop;

public enum GameLauncher
{
    Steam,
    Heroic,
    Gog,
    Itch,
}

/// <summary>Finds each PC game storefront client's own executable, if installed - not game
/// install paths, the launcher app itself (Steam.exe, Heroic's own portable exe,
/// GalaxyClient.exe, itch.exe), so GridShell can offer a quick-launch button for each and
/// (separately) know which exe to close once a game tile that launches through one of them
/// ends.</summary>
public static class GameLauncherDetector
{
    public static string? FindExe(GameLauncher launcher) => launcher switch
    {
        GameLauncher.Steam => FindSteam() ?? InstalledAppLocator.FindMainExe("Steam"),
        // Heroic installs as a standalone portable exe (GitHubReleaseInstaller), never through
        // a real installer - no registry entry to scan, unlike every other launcher here.
        GameLauncher.Heroic => InstalledAppLocator.FindBestExeInDirectory(
            GitHubReleaseInstaller.InstallRoot("heroicgameslauncher.heroic"), "Heroic"),
        GameLauncher.Gog => InstalledAppLocator.FindMainExe("GOG Galaxy"),
        GameLauncher.Itch => FindItch() ?? InstalledAppLocator.FindMainExe("itch"),
        _ => null,
    };

    /// <summary>HKCU\Software\Valve\Steam\SteamPath - confirmed against the actively-maintained
    /// GameFinder library's own SteamLocationFinder.cs, which reads this exact key/value to get
    /// Steam's install directory (Steam.exe always sits directly in that root). Only reliable
    /// once Steam has actually been run at least once (it writes this key itself, not the
    /// installer) - InstalledAppLocator.FindMainExe is the fallback for "installed but never
    /// opened yet", same as every other Store app.</summary>
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

    /// <summary>itch's desktop app is usually a per-user Squirrel-style install at this fixed
    /// path with no system registry entry - InstalledAppLocator.FindMainExe is the fallback for
    /// any install that doesn't match this exact assumption.</summary>
    private static string? FindItch()
    {
        var exe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "itch", "itch.exe");
        return File.Exists(exe) ? exe : null;
    }
}
