using System.IO;
using System.Linq;
using System.Text.Json;

namespace GridShell.Core.Widgets;

/// <summary>Persists the user's desktop widgets to %AppData%\GridShell\widgets.json - same
/// shape as TileStore, but with no DTO split: DesktopWidget has no non-serializable members
/// (unlike Tile's runtime-only Icon), so it serializes directly.</summary>
public static class WidgetStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GridShell",
        "widgets.json");

    public static List<DesktopWidget> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new List<DesktopWidget>();
            }

            var json = File.ReadAllText(StorePath);
            return JsonSerializer.Deserialize<List<DesktopWidget>>(json) ?? new List<DesktopWidget>();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<DesktopWidget>();
        }
    }

    public static void Save(IEnumerable<DesktopWidget> widgets)
    {
        var json = JsonSerializer.Serialize(widgets.ToList(), new JsonSerializerOptions { WriteIndented = true });

        var dir = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(StorePath, json);
    }
}
