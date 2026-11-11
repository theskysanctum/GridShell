using System.IO;
using System.Text.Json;

namespace GridShell.Core.Controllers;

/// <summary>Persists per-device Controller Manager preferences to
/// %AppData%\GridShell\controller-prefs.json, keyed by "VID:PID" hex - same shape/pattern as
/// WidgetStore/TileStore.</summary>
public static class ControllerPreferencesStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GridShell",
        "controller-prefs.json");

    public static string KeyFor(ushort vendorId, ushort productId) => $"{vendorId:X4}:{productId:X4}";

    public static Dictionary<string, ControllerPreferences> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new Dictionary<string, ControllerPreferences>();
            }

            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<Dictionary<string, ControllerPreferences>>(json)
                   ?? new Dictionary<string, ControllerPreferences>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new Dictionary<string, ControllerPreferences>();
        }
    }

    public static void Save(Dictionary<string, ControllerPreferences> preferences)
    {
        var json = JsonSerializer.Serialize(preferences, new JsonSerializerOptions { WriteIndented = true });
        var dir = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(StorePath, json);
    }
}
