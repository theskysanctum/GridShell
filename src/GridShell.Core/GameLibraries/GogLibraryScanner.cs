using System.IO;
using Microsoft.Win32;

namespace GridShell.Core.GameLibraries;

/// <summary>Finds installed GOG Galaxy games via the per-game registry keys it writes under
/// HKLM\Software\GOG.com\Games - verified against GameFinder (github.com/erri120/GameFinder),
/// an actively-maintained open-source library built for exactly this, not guessed. Launches
/// through GalaxyClient.exe itself (the "/command=runGame /gameId=" pattern, corroborated
/// across several independent real-world sources) since GOG's registry entries don't store a
/// direct launch executable.</summary>
public static class GogLibraryScanner
{
    public static List<InstalledGame> Scan()
    {
        var results = new List<InstalledGame>();

        var galaxyExe = GetGalaxyClientPath();
        if (galaxyExe is null)
        {
            return results;
        }

        using var gamesKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32)
            .OpenSubKey(@"Software\GOG.com\Games");
        if (gamesKey is null)
        {
            return results;
        }

        foreach (var gameId in gamesKey.GetSubKeyNames())
        {
            using var gameKey = gamesKey.OpenSubKey(gameId);
            var name = gameKey?.GetValue("gameName") as string;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            results.Add(new InstalledGame("GOG", name, galaxyExe, $"/command=runGame /gameId={gameId}"));
        }

        return results;
    }

    private static string? GetGalaxyClientPath()
    {
        using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\GOG.com\GalaxyClient\paths");
        var dir = key?.GetValue("client") as string;
        if (!string.IsNullOrWhiteSpace(dir))
        {
            var exe = Path.Combine(dir, "GalaxyClient.exe");
            if (File.Exists(exe))
            {
                return exe;
            }
        }

        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "GOG Galaxy", "GalaxyClient.exe");
        return File.Exists(fallback) ? fallback : null;
    }
}
