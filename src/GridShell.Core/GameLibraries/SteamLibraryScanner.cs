using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Win32;

namespace GridShell.Core.GameLibraries;

/// <summary>Finds installed Steam games by reading Steam's own on-disk manifests - no public
/// API for this, so verified against real-world file layout (libraryfolders.vdf lists every
/// library, each library's steamapps\appmanifest_&lt;appid&gt;.acf names one installed game),
/// not guessed. Steam's VDF ("KeyValues") format is simple key-value text, parsed here with
/// plain regex rather than a full parser since only a couple of flat fields are needed.</summary>
public static class SteamLibraryScanner
{
    public static List<InstalledGame> Scan()
    {
        var results = new List<InstalledGame>();

        var steamPath = GetSteamPath();
        if (steamPath is null)
        {
            return results;
        }

        foreach (var library in GetLibraryFolders(steamPath))
        {
            var steamappsDir = Path.Combine(library, "steamapps");
            if (!Directory.Exists(steamappsDir))
            {
                continue;
            }

            foreach (var manifestPath in SafeEnumerateFiles(steamappsDir, "appmanifest_*.acf"))
            {
                var (appId, name) = ParseAppManifest(manifestPath);
                if (appId is null || name is null)
                {
                    continue;
                }

                results.Add(new InstalledGame("Steam", name, $"steam://rungameid/{appId}", ""));
            }
        }

        return results;
    }

    private static string? GetSteamPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam");
        var path = key?.GetValue("SteamPath") as string;
        return string.IsNullOrWhiteSpace(path) ? null : path.Replace('/', '\\');
    }

    private static List<string> GetLibraryFolders(string steamPath)
    {
        var folders = new List<string> { steamPath };

        var vdfPath = Path.Combine(steamPath, "steamapps", "libraryfolders.vdf");
        if (!File.Exists(vdfPath))
        {
            return folders;
        }

        try
        {
            var text = File.ReadAllText(vdfPath);
            foreach (Match match in Regex.Matches(text, "\"path\"\\s*\"([^\"]+)\""))
            {
                var path = match.Groups[1].Value.Replace("\\\\", "\\");
                if (!folders.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    folders.Add(path);
                }
            }
        }
        catch (IOException)
        {
        }

        return folders;
    }

    private static (string? AppId, string? Name) ParseAppManifest(string path)
    {
        try
        {
            var text = File.ReadAllText(path);
            var appIdMatch = Regex.Match(text, "\"appid\"\\s*\"(\\d+)\"");
            var nameMatch = Regex.Match(text, "\"name\"\\s*\"([^\"]+)\"");
            return (
                appIdMatch.Success ? appIdMatch.Groups[1].Value : null,
                nameMatch.Success ? nameMatch.Groups[1].Value : null);
        }
        catch (IOException)
        {
            return (null, null);
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string dir, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(dir, pattern).ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return Enumerable.Empty<string>();
        }
    }
}
