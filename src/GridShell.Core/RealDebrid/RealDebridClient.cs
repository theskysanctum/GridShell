using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace GridShell.Core.RealDebrid;

public sealed record RealDebridUser(string Username, string Type, long PremiumSecondsRemaining);

public sealed record UnrestrictedLink(string Filename, long Filesize, string DownloadUrl);

public sealed record TorrentFileEntry(int Id, string Path, long Bytes, bool Selected);

public sealed record TorrentInfo(
    string Id,
    string Filename,
    string Status,
    int Progress,
    IReadOnlyList<TorrentFileEntry> Files,
    IReadOnlyList<string> Links);

/// <summary>Thin wrapper over Real-Debrid's REST API (https://api.real-debrid.com/rest/1.0) -
/// every endpoint/field used here was verified against RD's own published docs before use, not
/// guessed. Auth is a private API token (from real-debrid.com/apitoken), sent as a Bearer
/// token - RD's own docs note this is insecure for a publicly-distributed app, acceptable here
/// since GridShell only ever runs it locally for its own user.</summary>
public static class RealDebridClient
{
    private const string BaseUrl = "https://api.real-debrid.com/rest/1.0";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(20) };

    public static async Task<RealDebridUser?> GetUserAsync(string apiToken)
    {
        using var response = await SendAsync(HttpMethod.Get, "/user", apiToken);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        return new RealDebridUser(
            root.GetProperty("username").GetString() ?? "",
            root.GetProperty("type").GetString() ?? "",
            root.TryGetProperty("premium", out var premium) ? premium.GetInt64() : 0);
    }

    public static async Task<UnrestrictedLink?> UnrestrictLinkAsync(string apiToken, string link)
    {
        var form = new Dictionary<string, string> { ["link"] = link };
        using var response = await SendAsync(HttpMethod.Post, "/unrestrict/link", apiToken, form);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;
        return new UnrestrictedLink(
            root.GetProperty("filename").GetString() ?? "download",
            root.TryGetProperty("filesize", out var fs) ? fs.GetInt64() : 0,
            root.GetProperty("download").GetString() ?? "");
    }

    /// <summary>Returns the new torrent's id, or null on failure.</summary>
    public static async Task<string?> AddMagnetAsync(string apiToken, string magnet)
    {
        var form = new Dictionary<string, string> { ["magnet"] = magnet };
        using var response = await SendAsync(HttpMethod.Post, "/torrents/addMagnet", apiToken, form);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        return doc.RootElement.GetProperty("id").GetString();
    }

    public static async Task<TorrentInfo?> GetTorrentInfoAsync(string apiToken, string id)
    {
        using var response = await SendAsync(HttpMethod.Get, $"/torrents/info/{id}", apiToken);
        if (response is null || !response.IsSuccessStatusCode)
        {
            return null;
        }

        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = doc.RootElement;

        var files = new List<TorrentFileEntry>();
        if (root.TryGetProperty("files", out var filesArray))
        {
            foreach (var f in filesArray.EnumerateArray())
            {
                files.Add(new TorrentFileEntry(
                    f.GetProperty("id").GetInt32(),
                    f.GetProperty("path").GetString() ?? "",
                    f.GetProperty("bytes").GetInt64(),
                    f.GetProperty("selected").GetInt32() == 1));
            }
        }

        var links = new List<string>();
        if (root.TryGetProperty("links", out var linksArray))
        {
            foreach (var l in linksArray.EnumerateArray())
            {
                if (l.GetString() is { } s)
                {
                    links.Add(s);
                }
            }
        }

        return new TorrentInfo(
            root.GetProperty("id").GetString() ?? id,
            root.GetProperty("filename").GetString() ?? "",
            root.GetProperty("status").GetString() ?? "",
            root.TryGetProperty("progress", out var p) ? p.GetInt32() : 0,
            files,
            links);
    }

    public static async Task<bool> SelectAllFilesAsync(string apiToken, string id)
    {
        var form = new Dictionary<string, string> { ["files"] = "all" };
        using var response = await SendAsync(HttpMethod.Post, $"/torrents/selectFiles/{id}", apiToken, form);
        // 204 (selected) and 202 (already done) both mean the torrent is proceeding.
        return response is not null &&
            (response.StatusCode == System.Net.HttpStatusCode.NoContent ||
             (int)response.StatusCode == 202);
    }

    private static async Task<HttpResponseMessage?> SendAsync(
        HttpMethod method, string path, string apiToken, Dictionary<string, string>? form = null)
    {
        try
        {
            using var request = new HttpRequestMessage(method, BaseUrl + path);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiToken);
            if (form is not null)
            {
                request.Content = new FormUrlEncodedContent(form);
            }

            return await Http.SendAsync(request);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return null;
        }
    }
}
