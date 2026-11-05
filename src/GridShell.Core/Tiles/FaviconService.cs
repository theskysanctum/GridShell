using System.IO;
using System.Linq;
using System.Net.Http;

namespace GridShell.Core.Tiles;

/// <summary>Fetches a site's favicon via Google's public favicon endpoint and caches it on disk,
/// keyed by host, so it's only fetched once per site across app restarts.</summary>
public static class FaviconService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GridShell",
        "favicons");

    public static async Task<string?> GetCachedFaviconPathAsync(string url)
    {
        var host = GetHost(url);
        if (host is null)
        {
            return null;
        }

        Directory.CreateDirectory(CacheDir);
        var cachePath = Path.Combine(CacheDir, SanitizeFileName(host) + ".png");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        try
        {
            var faviconUrl = $"https://www.google.com/s2/favicons?sz=64&domain={Uri.EscapeDataString(host)}";
            var bytes = await Http.GetByteArrayAsync(faviconUrl);
            await File.WriteAllBytesAsync(cachePath, bytes);
            return cachePath;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }

    private static string? GetHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));
}
