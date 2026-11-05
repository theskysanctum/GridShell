using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GridShell.Core.Tiles;

/// <summary>Fetches a game's box-art cover from IGDB (via Twitch's OAuth-gated API) and caches it
/// on disk, keyed by game name, so it's only fetched once per game across app restarts — same
/// pattern as FaviconService for websites.</summary>
public static class IgdbCoverService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(8) };

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GridShell",
        "igdb-covers");

    private static string? _cachedAccessToken;
    private static DateTime _accessTokenExpiresAtUtc = DateTime.MinValue;

    public static async Task<string?> GetCachedCoverPathAsync(string gameName, string clientId, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        Directory.CreateDirectory(CacheDir);
        var cachePath = Path.Combine(CacheDir, SanitizeFileName(gameName) + ".jpg");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        try
        {
            var token = await GetAccessTokenAsync(clientId, clientSecret);
            if (token is null)
            {
                return null;
            }

            var imageId = await SearchCoverImageIdAsync(gameName, clientId, token);
            if (imageId is null)
            {
                return null;
            }

            var imageUrl = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg";
            var bytes = await Http.GetByteArrayAsync(imageUrl);
            await File.WriteAllBytesAsync(cachePath, bytes);
            return cachePath;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            return null;
        }
    }

    private static async Task<string?> GetAccessTokenAsync(string clientId, string clientSecret)
    {
        if (_cachedAccessToken is not null && DateTime.UtcNow < _accessTokenExpiresAtUtc)
        {
            return _cachedAccessToken;
        }

        var url = $"https://id.twitch.tv/oauth2/token?client_id={Uri.EscapeDataString(clientId)}" +
                   $"&client_secret={Uri.EscapeDataString(clientSecret)}&grant_type=client_credentials";

        using var response = await Http.PostAsync(url, content: null);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        var accessToken = root.GetProperty("access_token").GetString();
        var expiresInSeconds = root.GetProperty("expires_in").GetInt32();

        // A minute of slack so a token that's about to expire mid-call doesn't get reused.
        _accessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds - 60);
        _cachedAccessToken = accessToken;
        return accessToken;
    }

    private static async Task<string?> SearchCoverImageIdAsync(string gameName, string clientId, string accessToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
        request.Headers.Add("Client-ID", clientId);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

        // IGDB's own index is full of fan romhack/bundle/compilation entries that also match a
        // plain title search (e.g. "Super Mario Galaxy 2 2: Super Mario Galaxy 1") and often
        // outrank the real game in "search"'s relevance ordering, which can't be combined with a
        // server-side sort — fetch a handful of candidates with their rating_count instead, and
        // pick the most-rated one client-side, since an obscure fan entry essentially never has
        // one.
        var escapedName = gameName.Replace("\"", "\\\"");
        request.Content = new StringContent(
            $"search \"{escapedName}\"; fields name,cover.image_id,rating_count; limit 10;", Encoding.UTF8, "text/plain");

        using var response = await Http.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var results = doc.RootElement;
        if (results.GetArrayLength() == 0)
        {
            return null;
        }

        string? bestImageId = null;
        var bestRatingCount = -1;

        foreach (var candidate in results.EnumerateArray())
        {
            if (!candidate.TryGetProperty("cover", out var cover) || !cover.TryGetProperty("image_id", out var imageId))
            {
                continue;
            }

            var ratingCount = candidate.TryGetProperty("rating_count", out var rc) ? rc.GetInt32() : 0;
            if (ratingCount > bestRatingCount)
            {
                bestRatingCount = ratingCount;
                bestImageId = imageId.GetString();
            }
        }

        return bestImageId;
    }

    private static string SanitizeFileName(string name) =>
        string.Concat(name.Select(c => char.IsLetterOrDigit(c) || c is '.' or '-' ? c : '_'));

    /// <summary>A single IGDB search result offered for manual picking — unlike
    /// GetCachedCoverPathAsync's automatic best-by-rating_count choice, every match within a
    /// reasonable candidate window is returned so the user can pick the actual right one (the
    /// auto-pick can be wrong for a game with a common/reused title, a remaster vs. the
    /// original, a region-specific release, etc.).</summary>
    public sealed record IgdbCandidate(string Name, string ImageId, int? ReleaseYear);

    public static async Task<List<IgdbCandidate>> SearchCandidatesAsync(string query, string clientId, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret) || string.IsNullOrWhiteSpace(query))
        {
            return new List<IgdbCandidate>();
        }

        try
        {
            var token = await GetAccessTokenAsync(clientId, clientSecret);
            if (token is null)
            {
                return new List<IgdbCandidate>();
            }

            using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/games");
            request.Headers.Add("Client-ID", clientId);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var escapedQuery = query.Replace("\"", "\\\"");
            request.Content = new StringContent(
                $"search \"{escapedQuery}\"; fields name,cover.image_id,first_release_date,rating_count; limit 20;",
                Encoding.UTF8, "text/plain");

            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return new List<IgdbCandidate>();
            }

            using var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);

            var candidates = new List<IgdbCandidate>();
            foreach (var entry in doc.RootElement.EnumerateArray())
            {
                if (!entry.TryGetProperty("name", out var nameProp)
                    || !entry.TryGetProperty("cover", out var cover)
                    || !cover.TryGetProperty("image_id", out var imageIdProp))
                {
                    continue;
                }

                int? releaseYear = entry.TryGetProperty("first_release_date", out var releaseDate) && releaseDate.ValueKind == JsonValueKind.Number
                    ? DateTimeOffset.FromUnixTimeSeconds(releaseDate.GetInt64()).Year
                    : null;

                candidates.Add(new IgdbCandidate(nameProp.GetString() ?? "", imageIdProp.GetString() ?? "", releaseYear));
            }

            return candidates;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            return new List<IgdbCandidate>();
        }
    }

    /// <summary>Downloads one manually-chosen candidate's cover and caches it under its own
    /// image-id-keyed filename - deliberately separate from GetCachedCoverPathAsync's
    /// name-keyed cache, so picking a specific cover for one tile can't collide with or get
    /// silently overwritten by another game that happens to share the same display name.</summary>
    public static async Task<string?> DownloadCoverAsync(IgdbCandidate candidate)
    {
        try
        {
            Directory.CreateDirectory(CacheDir);
            var cachePath = Path.Combine(CacheDir, "manual_" + SanitizeFileName(candidate.ImageId) + ".jpg");
            if (!File.Exists(cachePath))
            {
                var imageUrl = $"https://images.igdb.com/igdb/image/upload/t_cover_big/{candidate.ImageId}.jpg";
                var bytes = await Http.GetByteArrayAsync(imageUrl);
                await File.WriteAllBytesAsync(cachePath, bytes);
            }

            return cachePath;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException)
        {
            return null;
        }
    }
}
