using System.IO;
using System.Linq;
using System.Net;
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
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GridShell",
        "igdb-covers");

    private static string? _cachedAccessToken;
    private static string? _cachedClientId;
    private static string? _cachedClientSecret;
    private static DateTime _accessTokenExpiresAtUtc = DateTime.MinValue;
    private static readonly Dictionary<string, int?> PlatformIdCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string?> ImageIdCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Resolves straight to the IGDB CDN's own public image URL - no byte download, no
    /// disk cache file - for callers that just hand it to an Image control (BitmapImage can
    /// download a remote UriSource on its own, same as StoreSceneSync's thumbnail URLs already
    /// do) rather than needing a local file path. Meant for a transient browsing view (the
    /// Store's Games category) where covers are looked at once and never again, not for Game
    /// tiles that reload the same cover every session - those still go through
    /// GetCachedCoverPathAsync, where a persistent disk cache actually earns its keep. Only the
    /// resolved image id is cached (in memory, for this process's lifetime) to avoid re-querying
    /// IGDB's search on every repeat view of the same platform within one session - the image
    /// bytes themselves are never kept.</summary>
    public static async Task<string?> GetCoverImageUrlAsync(string gameName, string clientId, string clientSecret, int? platformId = null)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        var cacheKey = SanitizeFileName(gameName) + (platformId is { } pid ? $"_p{pid}" : "");
        if (ImageIdCache.TryGetValue(cacheKey, out var cachedImageId))
        {
            return cachedImageId is null ? null : $"https://images.igdb.com/igdb/image/upload/t_cover_big/{cachedImageId}.jpg";
        }

        try
        {
            var token = await GetAccessTokenAsync(clientId, clientSecret);
            if (token is null)
            {
                return null;
            }

            var imageId = await SearchCoverImageIdAsync(gameName, clientId, token, platformId);
            ImageIdCache[cacheKey] = imageId;
            return imageId is null ? null : $"https://images.igdb.com/igdb/image/upload/t_cover_big/{imageId}.jpg";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }

    /// <summary>platformId, when given, narrows the IGDB search to that one platform (see
    /// ResolvePlatformIdAsync) - without it, the same title on a different system (e.g. a
    /// "Aladdin" released separately on Genesis and SNES, with different box art) can resolve
    /// to the wrong platform's cover purely by whichever has the higher rating_count. Baked
    /// into the cache filename too, so the same name on two different platforms never collides
    /// on one cached file.</summary>
    public static async Task<string?> GetCachedCoverPathAsync(string gameName, string clientId, string clientSecret, int? platformId = null)
    {
        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return null;
        }

        Directory.CreateDirectory(CacheDir);
        var cacheSuffix = platformId is { } pid ? $"_p{pid}" : "";
        var cachePath = Path.Combine(CacheDir, SanitizeFileName(gameName) + cacheSuffix + ".jpg");
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

            var imageId = await SearchCoverImageIdAsync(gameName, clientId, token, platformId);
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
        clientId = clientId.Trim();
        clientSecret = clientSecret.Trim();

        if (_cachedAccessToken is not null
            && string.Equals(_cachedClientId, clientId, StringComparison.Ordinal)
            && string.Equals(_cachedClientSecret, clientSecret, StringComparison.Ordinal)
            && DateTime.UtcNow < _accessTokenExpiresAtUtc)
        {
            return _cachedAccessToken;
        }

        var url = $"https://id.twitch.tv/oauth2/token?client_id={Uri.EscapeDataString(clientId)}" +
                   $"&client_secret={Uri.EscapeDataString(clientSecret)}&grant_type=client_credentials";

        using var response = await Http.PostAsync(url, content: null);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException("Twitch OAuth rejected the IGDB credentials.", null, response.StatusCode);
        }

        using var stream = await response.Content.ReadAsStreamAsync();
        using var doc = await JsonDocument.ParseAsync(stream);
        var root = doc.RootElement;
        var accessToken = root.GetProperty("access_token").GetString();
        var expiresInSeconds = root.GetProperty("expires_in").GetInt32();
        if (string.IsNullOrWhiteSpace(accessToken))
        {
            return null;
        }

        // A minute of slack so a token that's about to expire mid-call doesn't get reused.
        _accessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(expiresInSeconds - 60);
        _cachedAccessToken = accessToken;
        _cachedClientId = clientId;
        _cachedClientSecret = clientSecret;
        return accessToken;
    }

    /// <summary>Resolves a platform's IGDB numeric id by name, via IGDB's own /platforms search
    /// - not a hardcoded id table (EmulationPlatform.All alone is ~190 entries, and getting
    /// even one of those ids wrong from memory would be worse than not filtering at all).
    /// Cached in memory, so each distinct platform name this process ever asks about is only
    /// ever looked up once. Null means either no IGDB match or the lookup failed - callers
    /// already treat that the same as "don't filter by platform".</summary>
    public static async Task<int?> ResolvePlatformIdAsync(string platformName, string clientId, string clientSecret)
    {
        if (string.IsNullOrWhiteSpace(platformName))
        {
            return null;
        }

        if (PlatformIdCache.TryGetValue(platformName, out var cached))
        {
            return cached;
        }

        int? resolved = null;
        try
        {
            var token = await GetAccessTokenAsync(clientId, clientSecret);
            if (token is not null)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "https://api.igdb.com/v4/platforms");
                request.Headers.Add("Client-ID", clientId);
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

                var escaped = platformName.Replace("\"", "\\\"");
                request.Content = new StringContent(
                    $"search \"{escaped}\"; fields name; limit 1;", Encoding.UTF8, "text/plain");

                using var response = await Http.SendAsync(request);
                if (response.IsSuccessStatusCode)
                {
                    using var stream = await response.Content.ReadAsStreamAsync();
                    using var doc = await JsonDocument.ParseAsync(stream);
                    if (doc.RootElement.GetArrayLength() > 0 && doc.RootElement[0].TryGetProperty("id", out var idProp))
                    {
                        resolved = idProp.GetInt32();
                    }
                }
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or JsonException)
        {
            resolved = null;
        }

        PlatformIdCache[platformName] = resolved;
        return resolved;
    }

    private static async Task<string?> SearchCoverImageIdAsync(string gameName, string clientId, string accessToken, int? platformId)
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
        var whereClause = platformId is { } pid ? $" where platforms = ({pid});" : "";
        request.Content = new StringContent(
            $"search \"{escapedName}\";{whereClause} fields name,cover.image_id,rating_count; limit 10;", Encoding.UTF8, "text/plain");

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

    public sealed record IgdbSearchResult(IReadOnlyList<IgdbCandidate> Candidates, string? ErrorMessage);

    public static async Task<IgdbSearchResult> SearchCandidatesAsync(string query, string clientId, string clientSecret)
    {
        query = query.Trim();
        clientId = clientId.Trim();
        clientSecret = clientSecret.Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            return new IgdbSearchResult(Array.Empty<IgdbCandidate>(), "Enter a game title to search.");
        }

        if (string.IsNullOrWhiteSpace(clientId) || string.IsNullOrWhiteSpace(clientSecret))
        {
            return new IgdbSearchResult(Array.Empty<IgdbCandidate>(), "Enter your IGDB Client ID and Secret in Game Manager.");
        }

        try
        {
            var token = await GetAccessTokenAsync(clientId, clientSecret);
            if (token is null)
            {
                return new IgdbSearchResult(Array.Empty<IgdbCandidate>(), "Twitch did not return an access token. Check your IGDB credentials.");
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
                return new IgdbSearchResult(
                    Array.Empty<IgdbCandidate>(),
                    $"IGDB rejected the request (HTTP {(int)response.StatusCode} {response.StatusCode}). Check your Client ID and Secret.");
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

            return new IgdbSearchResult(candidates, null);
        }
        catch (HttpRequestException ex) when (ex.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            return new IgdbSearchResult(Array.Empty<IgdbCandidate>(), "Twitch rejected the IGDB credentials. Check the Client ID and Secret in Game Manager.");
        }
        catch (HttpRequestException)
        {
            return new IgdbSearchResult(Array.Empty<IgdbCandidate>(), "Couldn't connect to IGDB. Check your internet connection and try again.");
        }
        catch (TaskCanceledException)
        {
            return new IgdbSearchResult(Array.Empty<IgdbCandidate>(), "The IGDB lookup timed out. Please try again.");
        }
        catch (IOException)
        {
            return new IgdbSearchResult(Array.Empty<IgdbCandidate>(), "Couldn't read the IGDB response. Please try again.");
        }
        catch (JsonException)
        {
            return new IgdbSearchResult(Array.Empty<IgdbCandidate>(), "IGDB returned an invalid response. Please try again.");
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
