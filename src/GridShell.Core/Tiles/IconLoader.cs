using System.Collections.Concurrent;
using System.Windows.Media;
using GridShell.Core.Interop;
using GridShell.Core.Settings;

namespace GridShell.Core.Tiles;

/// <summary>Loads icons for tiles off the UI thread, with in-memory caching.
/// A user-chosen custom icon always wins; otherwise apps use their real shell icon, websites
/// use a fetched-and-disk-cached favicon (falling back to a generic icon), and games use a
/// fetched-and-disk-cached IGDB cover when credentials are set (falling back to the .exe icon
/// the same way apps work, so an unconfigured/offline/no-match lookup degrades gracefully).</summary>
public static class IconLoader
{
    private static readonly ConcurrentDictionary<string, ImageSource?> Cache =
        new(StringComparer.OrdinalIgnoreCase);

    public static async Task LoadIconAsync(Tile tile, AppSettings? settings = null)
    {
        // Game-kind tiles can't be keyed by IconSourcePath like apps — for an emulated game
        // that's the emulator's own exe path, shared identically across every ROM launched
        // through it, which would otherwise collapse every such game onto one cached icon.
        var cacheKey = tile.CustomIconPath ?? tile.Kind switch
        {
            TileKind.Website => "fav:" + tile.TargetPath,
            TileKind.Game => "game:" + tile.Name,
            _ => tile.IconSourcePath,
        };

        if (Cache.TryGetValue(cacheKey, out var cached))
        {
            tile.Icon = cached;
            return;
        }

        var icon = await ResolveIconAsync(tile, settings);
        Cache[cacheKey] = icon;
        tile.Icon = icon;
    }

    private static async Task<ImageSource?> ResolveIconAsync(Tile tile, AppSettings? settings)
    {
        if (tile.CustomIconPath is { } customPath)
        {
            return await Task.Run(() => ImageLoader.LoadFromFile(customPath));
        }

        if (tile.Kind == TileKind.Website)
        {
            var faviconPath = await FaviconService.GetCachedFaviconPathAsync(tile.TargetPath);
            return faviconPath is not null
                ? await Task.Run(() => ImageLoader.LoadFromFile(faviconPath))
                : await Task.Run(() => ShellIcons.GetGenericIcon(".url"));
        }

        if (tile.Kind == TileKind.Game && settings is { IgdbClientId: { Length: > 0 } clientId, IgdbClientSecret: { Length: > 0 } clientSecret })
        {
            var coverPath = await IgdbCoverService.GetCachedCoverPathAsync(tile.Name, clientId, clientSecret);
            if (coverPath is not null)
            {
                return await Task.Run(() => ImageLoader.LoadFromFile(coverPath));
            }
        }

        return await Task.Run(() => ShellIcons.GetIcon(tile.IconSourcePath));
    }
}
