using GridShell.Core.Tiles;

namespace GridShell.Core.Store;

/// <summary>After a Store app installs (or is confirmed already installed), adds an Apps-tab
/// Tile pointing at its real exe - same exePath resolution EmulatorAutoWire already uses
/// (InstalledAppLocator against the Uninstall registry for winget installs, or against
/// GitHubReleaseInstaller's own extraction folder for github-method ones), so a Store install
/// actually shows up as something launchable rather than just being present on disk. Never
/// duplicates a tile that already points at the same exe - repeat installs/opens of the Store
/// are idempotent.</summary>
public static class StoreTileAutoWire
{
    /// <summary>Returns the newly-added Tile (still iconless - the caller is expected to kick
    /// off IconLoader.LoadIconAsync itself, same as every other place a Tile is created, since
    /// IconLoader needs a live AppSettings for IGDB/favicon lookups that this class, living in
    /// GridShell.Core.Store rather than the UI layer, has no reason to depend on) or null if
    /// nothing was added (no resolvable exe yet, or a tile already points at it).</summary>
    public static Tile? TryWire(StoreApp app, List<Tile> allTiles)
    {
        var exePath = app.InstallMethod == "github"
            ? InstalledAppLocator.FindBestExeInDirectory(GitHubReleaseInstaller.InstallRoot(app.Id))
            : InstalledAppLocator.FindMainExe(app.Name);

        if (exePath is null)
        {
            return null;
        }

        if (allTiles.Any(t => t.Kind == TileKind.App && t.TargetPath.Equals(exePath, StringComparison.OrdinalIgnoreCase)))
        {
            return null;
        }

        var tile = new Tile
        {
            Name = app.Name,
            TargetPath = exePath,
            IconSourcePath = exePath,
            Kind = TileKind.App,
        };

        allTiles.Add(tile);
        return tile;
    }
}
