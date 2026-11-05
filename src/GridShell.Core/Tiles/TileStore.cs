using System.IO;
using System.Linq;
using System.Text.Json;

namespace GridShell.Core.Tiles;

/// <summary>Persists the user's manually-curated tiles to %AppData%\GridShell\tiles.json.</summary>
public static class TileStore
{
    private static readonly string StorePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GridShell",
        "tiles.json");

    public static List<Tile> Load()
    {
        try
        {
            if (!File.Exists(StorePath))
            {
                return new List<Tile>();
            }

            var json = File.ReadAllText(StorePath);
            var records = JsonSerializer.Deserialize<List<TileRecord>>(json) ?? new List<TileRecord>();
            return records.Select(r => r.ToTile()).ToList();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            return new List<Tile>();
        }
    }

    public static void Save(IEnumerable<Tile> tiles)
    {
        var records = tiles.Select(TileRecord.FromTile).ToList();
        var json = JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true });

        var dir = Path.GetDirectoryName(StorePath)!;
        Directory.CreateDirectory(dir);
        File.WriteAllText(StorePath, json);
    }
}
