using System.IO;
using GridShell.Core.Tiles;

namespace GridShell.Core.Store;

/// <summary>After a Store app installs (or is confirmed already installed), adds an Apps-tab
/// Tile pointing at its real exe - same exePath resolution EmulatorAutoWire already uses
/// (InstalledAppLocator against the Uninstall registry for winget installs, or against
/// GitHubReleaseInstaller's own extraction folder for github-method ones), so a Store install
/// actually shows up as something launchable rather than just being present on disk. Never
/// duplicates a tile that already points at the same exe - repeat installs/opens of the Store
/// are idempotent. Emulators (Category "Emulation") are deliberately excluded - they belong to
/// Game Manager's own world (EmulatorAutoWire wires them for actual game-launching, and Game
/// Manager's Launch button already opens one directly), not the Apps tab. Game-store launchers
/// (Category "Gaming" - Steam, Epic, GOG, itch) are excluded too: GameLibraryScanner already
/// pulls their installed games straight into the Games tab, so the launcher itself showing up
/// as an App tile too is redundant, not useful.</summary>
public static class StoreTileAutoWire
{
    /// <summary>Returns the Tile if something changed that needs an icon reload - either a
    /// brand new tile, or an existing one that just picked up a bundled Store icon it didn't
    /// have before (see StoreIconCache) - or null if nothing changed (no resolvable exe yet, the
    /// app is an emulator or game-store launcher, or a matching tile already has its icon
    /// settled). The caller is expected to kick off IconLoader.LoadIconAsync itself, same as
    /// every other place a Tile is created, since IconLoader needs a live AppSettings for
    /// IGDB/favicon lookups that this class, living in GridShell.Core.Store rather than the UI
    /// layer, has no reason to depend on.</summary>
    public static Tile? TryWire(StoreApp app, List<Tile> allTiles)
    {
        if (app.Category is "Emulation" or "Gaming")
        {
            return null;
        }

        var exePath = app.InstallMethod == "github"
            ? InstalledAppLocator.FindBestExeInDirectory(GitHubReleaseInstaller.InstallRoot(app.Id), app.Name)
            : InstalledAppLocator.FindMainExe(app.Name);

        if (exePath is null)
        {
            return null;
        }

        var existing = allTiles.FirstOrDefault(t =>
            t.Kind == TileKind.App && t.TargetPath.Equals(exePath, StringComparison.OrdinalIgnoreCase));

        var tile = existing;
        var isNew = false;
        if (tile is null)
        {
            tile = new Tile
            {
                Name = app.Name,
                TargetPath = exePath,
                IconSourcePath = exePath,
                Kind = TileKind.App,
            };
            allTiles.Add(tile);
            isNew = true;
        }

        // Prefer the Store's own bundled artwork over shell-icon extraction, which has proven
        // unreliable for some installs - but never clobber a user's own "Change Icon..." choice.
        var gotIcon = false;
        if ((tile.CustomIconPath is null || !File.Exists(tile.CustomIconPath))
            && StoreIconCache.GetOrCacheIconPath(app.Id) is { } cachedIconPath)
        {
            tile.CustomIconPath = cachedIconPath;
            gotIcon = true;
        }

        return isNew || gotIcon ? tile : null;
    }
}
