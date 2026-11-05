using System.IO;
using System.Linq;
using System.Text.Json;

namespace GridShell.Core.GameLibraries;

/// <summary>Finds installed Epic Games Launcher games by reading its own per-game ".item"
/// manifests - plain JSON, one per installed game, path and field names verified against the
/// actual files Epic's launcher writes (not guessed). Launches via Epic's own documented
/// protocol-activation URL (dev.epicgames.com/docs/epic-games-store/protocol-activation).</summary>
public static class EpicLibraryScanner
{
    private const string ManifestsDir = @"C:\ProgramData\Epic\EpicGamesLauncher\Data\Manifests";

    public static List<InstalledGame> Scan()
    {
        var results = new List<InstalledGame>();
        if (!Directory.Exists(ManifestsDir))
        {
            return results;
        }

        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(ManifestsDir, "*.item").ToList();
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
        {
            return results;
        }

        foreach (var file in files)
        {
            try
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(file));
                var root = doc.RootElement;

                var displayName = GetString(root, "DisplayName");
                var appName = GetString(root, "AppName");
                if (string.IsNullOrWhiteSpace(displayName) || string.IsNullOrWhiteSpace(appName))
                {
                    continue;
                }

                var catalogNamespace = GetString(root, "CatalogNamespace") ?? "";
                var catalogItemId = GetString(root, "CatalogItemId") ?? "";

                var launchUrl = $"com.epicgames.launcher://apps/{catalogNamespace}%3A{catalogItemId}%3A{appName}?action=launch&silent=true";
                results.Add(new InstalledGame("Epic", displayName, launchUrl, ""));
            }
            catch (Exception ex) when (ex is IOException or JsonException)
            {
            }
        }

        return results;
    }

    private static string? GetString(JsonElement root, string property) =>
        root.TryGetProperty(property, out var value) ? value.GetString() : null;
}
