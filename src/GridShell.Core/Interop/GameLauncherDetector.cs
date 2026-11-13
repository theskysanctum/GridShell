using System.IO;
using GridShell.Core.Store;
using Microsoft.Win32;

namespace GridShell.Core.Interop;

public enum GameLauncher
{
    Steam,
    Heroic,
    Itch,
}

/// <summary>Finds each PC game storefront client's own executable, if installed - not game
/// install paths, the launcher app itself (Steam.exe, Heroic's own portable exe, itch.exe), so
/// GridShell can offer a quick-launch button for each and (separately) know which exe to close
/// once a game tile that launches through one of them ends. Heroic covers both Epic and GOG
/// itself (plus Amazon Games) - neither gets its own separate launcher entry here.</summary>
public static class GameLauncherDetector
{
    public static string? FindExe(GameLauncher launcher) => launcher switch
    {
        GameLauncher.Steam => FindSteam() ?? InstalledAppLocator.FindMainExe("Steam"),
        // Steam/Heroic/itch all install via BrowserInstaller now (Install just opens each
        // one's own download page) - wherever the user's own installer puts them, which for
        // Heroic means its real Setup.exe, not a GridShell-managed folder. The same registry
        // scan every normally-installed app uses is the only thing that still makes sense here.
        GameLauncher.Heroic => InstalledAppLocator.FindMainExe("Heroic"),
        GameLauncher.Itch => InstalledAppLocator.FindMainExe("itch"),
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
}
