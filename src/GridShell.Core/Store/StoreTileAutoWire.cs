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
    public static bool TryWire(StoreApp app, List<Tile> allTiles)
    {
        var exePath = app.InstallMethod == "github"
            ? InstalledAppLocator.FindBestExeInDirectory(GitHubReleaseInstaller.InstallRoot(app.Id))
            : InstalledAppLocator.FindMainExe(app.Name);

        if (exePath is null)
        {
            return false;
        }

        if (allTiles.Any(t => t.Kind == TileKind.App && t.TargetPath.Equals(exePath, StringComparison.OrdinalIgnoreCase)))
        {
            return false;
        }

        allTiles.Add(new Tile
        {
            Name = app.Name,
            TargetPath = exePath,
            IconSourcePath = exePath,
            Kind = TileKind.App,
        });

        return true;
    }
}
