using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading.Tasks;

namespace GridShell.Core.Store;

/// <summary>Scenes, unlike the app catalog, are fetched directly over plain HTTP from
/// raw.githubusercontent.com - no git clone. Confirmed live: raw.githubusercontent.com (and
/// jsDelivr's gh mirror, checked as an alternative) both force Content-Type: text/plain plus a
/// locked-down Content-Security-Policy ("default-src 'none'; sandbox") on .html files
/// specifically, so a browser navigated straight at one won't render it - but a plain HttpClient
/// GET of the same URL gets the real bytes back fine, since that restriction only applies to
/// browser navigation/rendering, not raw byte fetches. So: fetch the bytes here, write them to a
/// small local cache, then hand WallpaperWindow a normal file:// path - the only genuinely new
/// download for a scene is that one small HTML file, not the whole repo.</summary>
public static class StoreSceneSync
{
    private const string RawBaseUrl = "https://raw.githubusercontent.com/theskysanctum/gridshell/master/store-catalog/scenes/";
    private const string CatalogUrl = "https://raw.githubusercontent.com/theskysanctum/gridshell/master/store-catalog/scenes.json";

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GridShell", "ScenesCache");

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GridShell-Store", "1.0"));
        return client;
    }

    /// <summary>A raw.githubusercontent.com URL a WPF Image can bind BitmapImage.UriSource to
    /// directly - PNG thumbnails don't hit the text/plain-forcing behavior .html does (confirmed
    /// live: image/png, normal CSP), so these load straight from the CDN with no local caching
    /// needed at all.</summary>
    public static string GetThumbnailUrl(string sceneId) => $"{RawBaseUrl}{sceneId}.png";

    /// <summary>Fetches store-catalog/scenes.json fresh every call - it's a few hundred bytes,
    /// not worth caching. Falls back to an empty catalog on any network failure rather than
    /// throwing, same "stale/empty is fine, don't crash the Store window" spirit as
    /// StoreCatalogSync's app catalog sync.</summary>
    public static async Task<StoreSceneCatalogFile> FetchCatalogAsync()
    {
        try
        {
            var json = await Http.GetStringAsync(CatalogUrl);
            return JsonSerializer.Deserialize<StoreSceneCatalogFile>(json) ?? new StoreSceneCatalogFile();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            return new StoreSceneCatalogFile();
        }
    }

    /// <summary>Where DownloadEntryAsync would put (or has already put) this scene's cached
    /// file - doesn't touch the network or the filesystem, just computes the path, so callers
    /// can compare it against AppSettings.WallpaperUrl (e.g. "is this the current wallpaper?")
    /// without triggering a download just to check.</summary>
    public static string GetLocalCachePath(string entryFile) => Path.Combine(CacheDir, entryFile);

    /// <summary>Downloads one scene's entry HTML (only that file - not the catalog, not the
    /// other scenes) into a small local cache keyed by filename, skipping the fetch entirely if
    /// already cached from a previous Apply. Returns the local file:// -navigable path, or null
    /// if the download failed (no internet, file renamed/removed upstream, etc.).</summary>
    public static async Task<string?> DownloadEntryAsync(string entryFile)
    {
        Directory.CreateDirectory(CacheDir);
        var localPath = GetLocalCachePath(entryFile);

        if (File.Exists(localPath))
        {
            return localPath;
        }

        try
        {
            var bytes = await Http.GetByteArrayAsync($"{RawBaseUrl}{entryFile}");
            await File.WriteAllBytesAsync(localPath, bytes);
            return localPath;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return null;
        }
    }
}
