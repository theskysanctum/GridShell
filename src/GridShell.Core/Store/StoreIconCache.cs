using System.IO;

namespace GridShell.Core.Store;

/// <summary>Copies a catalog app's bundled icon (StoreCatalogSync.GetIconPath, inside the
/// StoreCatalogSync clone that gets deleted/re-cloned on every sync) into a stable,
/// GridShell-owned cache folder - a Tile's CustomIconPath needs a path that outlives that
/// clone. Used to seed an Apps-tab tile's icon from the Store's own bundled artwork instead of
/// shell-icon extraction, which has proven unreliable for some installs (wrong exe picked,
/// helper exes with no embedded icon, etc. - see InstalledAppLocator's name-match fix).</summary>
public static class StoreIconCache
{
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GridShell", "TileIcons");

    /// <summary>Copies the catalog's bundled icon for appId into the stable cache (skipping the
    /// copy if already cached), returning the cached path - or null if the catalog has no
    /// bundled icon for this app.</summary>
    public static string? GetOrCacheIconPath(string appId)
    {
        var sourcePath = StoreCatalogSync.GetIconPath(appId);
        if (sourcePath is null)
        {
            return null;
        }

        Directory.CreateDirectory(CacheDir);
        var cachedPath = Path.Combine(CacheDir, $"{appId}.png");

        if (!File.Exists(cachedPath))
        {
            File.Copy(sourcePath, cachedPath, overwrite: true);
        }

        return cachedPath;
    }
}
