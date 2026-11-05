using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace GridShell.Core.Downloads;

public sealed record Aria2Status(string Status, long TotalLength, long CompletedLength, long DownloadSpeed, string? ErrorMessage);

/// <summary>Thin JSON-RPC client for a locally-running aria2c daemon (started in --enable-rpc
/// mode) - every method/param shape here was verified against aria2's own manual
/// (aria2.github.io/manual) before use. The secret is passed as "token:&lt;secret&gt;", the
/// first element of each call's params array - that's aria2's own auth convention, not an
/// HTTP header.</summary>
public static class Aria2Client
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public static async Task<string?> AddUriAsync(int port, string secret, string url, string dir, string fileName)
    {
        var body = BuildRequest("aria2.addUri", new object[]
        {
            $"token:{secret}",
            new[] { url },
            new Dictionary<string, string> { ["dir"] = dir, ["out"] = fileName },
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

    // aria2's RPC returns these as JSON strings, not numbers - confirmed against its manual.
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
