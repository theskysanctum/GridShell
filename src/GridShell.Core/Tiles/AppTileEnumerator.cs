using Vanara.PInvoke;
using Vanara.Windows.Shell;
using static Vanara.PInvoke.Shell32;

namespace GridShell.Core.Tiles;

/// <summary>
/// Enumerates the shell:AppsFolder virtual folder — the same source Windows' own Start menu
/// "All apps" list is built from. Covers everything a Start-Menu-folder (.lnk/.url) scan would,
/// plus apps that have no classic shortcut at all: UWP/Store apps, and apps packaged with
/// Squirrel or similar installers that register directly (Discord, GitHub Desktop, etc.) — a
/// gap the original .lnk-only scan had.
/// </summary>
public static class AppTileEnumerator
{
    public const string ShellAppsFolderPrefix = "shell:AppsFolder\\";

    public static IReadOnlyList<Tile> EnumerateAppTiles()
    {
        using var appsFolder = new ShellFolder(KNOWNFOLDERID.FOLDERID_AppsFolder);

        var tiles = new List<Tile>();
        foreach (var item in appsFolder)
        {
            using (item)
            {
                Tile? tile;
                try
                {
                    var name = item.GetDisplayName(ShellItemDisplayString.NormalDisplay) ?? "(unknown)";
                    var relative = item.GetDisplayName(ShellItemDisplayString.ParentRelativeParsing);
                    var shellPath = ShellAppsFolderPrefix + relative;

                    tile = new Tile
                    {
                        Name = name,
                        TargetPath = shellPath,
                        IconSourcePath = shellPath,
                        Kind = TileKind.App,
                    };
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    tile = null;
                }

                if (tile is not null)
                {
                    tiles.Add(tile);
                }
            }
        }

        return tiles
            .OrderBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
    }
}
