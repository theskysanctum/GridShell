using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace GridShell.Core.Downloads;

public sealed record Aria2Status(string Status, long TotalLength, long CompletedLength, long DownloadSpeed, string? ErrorMessage);

public static class Aria2Client
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<string?> AddUriAsync(int port, string secret, string url, string dir, string fileName, string? cookies = null)
    {
        var options = new Dictionary<string, string>
        {
            ["dir"] = dir,
            ["out"] = fileName,
        };

        // The saved session is for Archive.org only. Never attach it to an unrelated URL,
        // even if a caller accidentally supplies cookies for a different download.
        if (!string.IsNullOrWhiteSpace(cookies) && ArchiveOrgSession.IsArchiveOrgUrl(url))
        {
            options["header"] = $"Cookie: {cookies}";
        }

        var body = BuildRequest("aria2.addUri", new object[]
        {
            $"token:{secret}",
            new[] { url },
            options,
        });

        var result = await SendAsync(port, body);
        return result?.GetString();
    }

    public static async Task<Aria2Status?> TellStatusAsync(int port, string secret, string gid)
    {
        var body = BuildRequest("aria2.tellStatus", new object[] { $"token:{secret}", gid });
        var result = await SendAsync(port, body);
        if (result is not { } r)
        {
            return null;
        }

        return new Aria2Status(
            r.TryGetProperty("status", out var status) ? status.GetString() ?? "" : "",
            ParseLong(r, "totalLength"),
            ParseLong(r, "completedLength"),
            ParseLong(r, "downloadSpeed"),
            r.TryGetProperty("errorMessage", out var err) ? err.GetString() : null);
    }

    public static async Task ForceRemoveAsync(int port, string secret, string gid)
    {
        var body = BuildRequest("aria2.forceRemove", new object[] { $"token:{secret}", gid });
        await SendAsync(port, body);
    }

    private static long ParseLong(JsonElement element, string property) =>
        element.TryGetProperty(property, out var value) && long.TryParse(value.GetString(), out var parsed) ? parsed : 0;

    private static string BuildRequest(string method, object[] parameters) =>
        JsonSerializer.Serialize(new { jsonrpc = "2.0", id = Guid.NewGuid().ToString(), method, @params = parameters });

    private static async Task<JsonElement?> SendAsync(int port, string jsonBody)
    {
        try
        {
            using var content = new StringContent(jsonBody, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync($"http://127.0.0.1:{port}/jsonrpc", content);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            return doc.RootElement.TryGetProperty("result", out var result) ? result.Clone() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return null;
        }
    }
}
