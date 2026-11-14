using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GridShell.Core.Tiles;

public static class TheGamesDbCoverService
{
    private const string ApiBaseUrl = "https://api.thegamesdb.net/";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private static readonly string CacheDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "GridShell",
        "thegamesdb-covers");

    public sealed record Candidate(
        string Name,
        string PlatformName,
        int? ReleaseYear,
        string CoverUrl,
        string ThumbnailUrl);

    public sealed record SearchResult(IReadOnlyList<Candidate> Candidates, string? ErrorMessage);

    public static async Task<SearchResult> SearchCandidatesAsync(string query, string apiKey, string? preferredPlatform)
    {
        query = query.Trim();
        apiKey = apiKey.Trim();

        if (string.IsNullOrWhiteSpace(query))
        {
            return Failure("Enter a game title to search.");
        }

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Failure("Enter your TheGamesDB API key in Game Manager.");
        }

        try
        {
            var searchUrl = new Uri(
                new Uri(ApiBaseUrl),
                $"v1.1/Games/ByGameName?apikey={Uri.EscapeDataString(apiKey)}" +
                $"&name={Uri.EscapeDataString(query)}&include=boxart,platform&page=1");
            using var response = await Http.GetAsync(searchUrl);
            if (response.StatusCode == HttpStatusCode.Forbidden)
            {
                return Failure("TheGamesDB rejected the API key or the API request allowance has been reached.");
            }

            if (!response.IsSuccessStatusCode)
            {
                return Failure($"TheGamesDB search failed (HTTP {(int)response.StatusCode}).");
            }

            var json = await response.Content.ReadAsStringAsync();
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetObjectProperty(root, "data", out var data)
                || !TryGetObjectProperty(data, "games", out var games)
                || games.ValueKind != JsonValueKind.Array)
            {
                return Failure("TheGamesDB returned an unexpected response.");
            }

            var platformNames = ReadPlatformNames(root);
            var candidates = new List<Candidate>();
            foreach (var game in games.EnumerateArray())
            {
                var gameId = ReadString(game, "id");
                var name = ReadString(game, "game_title");
                if (gameId is null || string.IsNullOrWhiteSpace(name)
                    || !TryGetFrontBoxArt(root, gameId, out var coverUrl, out var thumbnailUrl))
                {
                    continue;
                }

                var platformId = ReadString(game, "platform");
                var platformName = platformId is not null && platformNames.TryGetValue(platformId, out var resolvedName)
                    ? resolvedName
                    : "";
                int? releaseYear = DateTime.TryParse(
                    ReadString(game, "release_date"),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out var releaseDate)
                    ? releaseDate.Year
                    : null;

                candidates.Add(new Candidate(name, platformName, releaseYear, coverUrl, thumbnailUrl));
            }

            var normalizedPlatform = Normalize(preferredPlatform ?? "");
            if (normalizedPlatform.Length > 0)
            {
                candidates = candidates
                    .OrderByDescending(candidate =>
                    {
                        var candidatePlatform = Normalize(candidate.PlatformName);
                        return candidatePlatform.Length > 0
                            && (candidatePlatform.Contains(normalizedPlatform, StringComparison.Ordinal)
                                || normalizedPlatform.Contains(candidatePlatform, StringComparison.Ordinal));
                    })
                    .ToList();
            }

            return new SearchResult(candidates, null);
        }
        catch (HttpRequestException)
        {
            return Failure("Couldn't connect to TheGamesDB. Check your internet connection and try again.");
        }
        catch (TaskCanceledException)
        {
            return Failure("TheGamesDB lookup timed out. Please try again.");
        }
        catch (IOException)
        {
            return Failure("Couldn't read the TheGamesDB response. Please try again.");
        }
        catch (JsonException)
        {
            return Failure("TheGamesDB returned invalid data. Please try again.");
        }
    }

    public static async Task<string?> DownloadCoverAsync(Candidate candidate)
    {
        if (!Uri.TryCreate(candidate.CoverUrl, UriKind.Absolute, out var coverUri)
            || coverUri.Scheme != Uri.UriSchemeHttps)
        {
            return null;
        }

        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(coverUri.AbsoluteUri))).ToLowerInvariant();
        var cachePath = Path.Combine(CacheDir, cacheKey + ".jpg");
        if (File.Exists(cachePath))
        {
            return cachePath;
        }

        try
        {
            Directory.CreateDirectory(CacheDir);
            using var response = await Http.GetAsync(coverUri);
            response.EnsureSuccessStatusCode();
            await File.WriteAllBytesAsync(cachePath, await response.Content.ReadAsByteArrayAsync());
            return cachePath;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static Dictionary<string, string> ReadPlatformNames(JsonElement root)
    {
        var names = new Dictionary<string, string>(StringComparer.Ordinal);
        if (TryGetObjectProperty(root, "include", out var include)
            && TryGetObjectProperty(include, "platform", out var platform)
            && TryGetObjectProperty(platform, "data", out var data)
            && data.ValueKind == JsonValueKind.Object)
        {
            foreach (var entry in data.EnumerateObject())
            {
                var name = ReadString(entry.Value, "name");
                if (!string.IsNullOrWhiteSpace(name))
                {
                    names[entry.Name] = name;
                }
            }
        }

        return names;
    }

    private static bool TryGetFrontBoxArt(
        JsonElement root,
        string gameId,
        out string coverUrl,
        out string thumbnailUrl)
    {
        coverUrl = "";
        thumbnailUrl = "";
        if (!TryGetObjectProperty(root, "include", out var include)
            || !TryGetObjectProperty(include, "boxart", out var boxart)
            || !TryGetObjectProperty(boxart, "data", out var data)
            || !TryGetObjectProperty(data, gameId, out var images)
            || images.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var image = images.EnumerateArray().FirstOrDefault(candidate =>
            string.Equals(ReadString(candidate, "type"), "boxart", StringComparison.OrdinalIgnoreCase)
            && string.Equals(ReadString(candidate, "side"), "front", StringComparison.OrdinalIgnoreCase));
        if (image.ValueKind == JsonValueKind.Undefined)
        {
            image = images.EnumerateArray().FirstOrDefault(candidate =>
                string.Equals(ReadString(candidate, "type"), "boxart", StringComparison.OrdinalIgnoreCase));
        }

        if (image.ValueKind != JsonValueKind.Object
            || !TryGetObjectProperty(boxart, "base_url", out var baseUrls))
        {
            return false;
        }

        var filename = ReadString(image, "filename");
        var originalBase = ReadString(baseUrls, "original");
        var thumbnailBase = ReadString(baseUrls, "small") ?? ReadString(baseUrls, "thumb");
        if (filename is null
            || !Uri.TryCreate(originalBase, UriKind.Absolute, out var originalBaseUri)
            || !Uri.TryCreate(thumbnailBase, UriKind.Absolute, out var thumbnailBaseUri)
            || originalBaseUri.Scheme != Uri.UriSchemeHttps
            || thumbnailBaseUri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        if (!Uri.TryCreate(originalBaseUri, filename, out var originalUri)
            || !Uri.TryCreate(thumbnailBaseUri, filename, out var thumbnailUri)
            || originalUri.Scheme != Uri.UriSchemeHttps
            || thumbnailUri.Scheme != Uri.UriSchemeHttps)
        {
            return false;
        }

        coverUrl = originalUri.AbsoluteUri;
        thumbnailUrl = thumbnailUri.AbsoluteUri;
        return true;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!TryGetObjectProperty(element, propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null,
        };
    }

    private static bool TryGetObjectProperty(JsonElement element, string propertyName, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty(propertyName, out value))
        {
            return true;
        }

        value = default;
        return false;
    }

    private static string Normalize(string value) =>
        string.Concat(value.Where(char.IsLetterOrDigit)).ToLowerInvariant();

    private static SearchResult Failure(string message) => new(Array.Empty<Candidate>(), message);
}
