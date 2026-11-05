using System.Linq;

namespace GridShell.Core.Tiles;

/// <summary>Serializable form of a Tile (no Icon — that's re-extracted/re-fetched at load time).</summary>
public class TileRecord
{
    public required string Name { get; init; }
    public required string TargetPath { get; init; }
    public required TileKind Kind { get; init; }
    public required string IconSourcePath { get; init; }
    public string? BrowserExecutablePath { get; init; }
    public string? CustomIconPath { get; init; }
    public string? Arguments { get; init; }
    public string? Platform { get; init; }

    public static TileRecord FromTile(Tile tile) => new()
    {
        Name = tile.Name,
        TargetPath = tile.TargetPath,
        Kind = tile.Kind,
        IconSourcePath = tile.IconSourcePath,
        BrowserExecutablePath = tile.BrowserExecutablePath,
        CustomIconPath = tile.CustomIconPath,
        Arguments = tile.Arguments,
        Platform = tile.Platform,
    };

    public Tile ToTile() => new()
    {
        Name = Name,
        TargetPath = TargetPath,
        Kind = Kind,
        IconSourcePath = IconSourcePath,
        BrowserExecutablePath = BrowserExecutablePath,
        CustomIconPath = CustomIconPath,
        Arguments = Arguments,
        Platform = Platform ?? InferPlatformFromRomPath(Arguments),
    };

    /// <summary>One-time migration for a Game tile saved before Tile.Platform existed - its
    /// launch Arguments already embeds the ROM's full path under &lt;root&gt;\&lt;slug&gt;\..., so
    /// the platform can be recovered from that instead of falling back to "PC" and losing its
    /// grouping in the Games tab.</summary>
    private static string? InferPlatformFromRomPath(string? arguments)
    {
        if (string.IsNullOrEmpty(arguments))
        {
            return null;
        }

        return EmulationPlatforms.All
            .FirstOrDefault(p => arguments.Contains($@"\{p.Slug}\", StringComparison.OrdinalIgnoreCase))
            ?.Slug;
    }
}
