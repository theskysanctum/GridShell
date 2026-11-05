using System.Net.Http;
using System.Text.Json;

namespace GridShell.Core.RetroAchievements;

public sealed record RetroAchievementsLoginResult(bool Success, string? Username, string? Token);

/// <summary>Logs into RetroAchievements.org to exchange a username+password for the persistent
/// API token every supporting emulator actually stores (RetroArch/Dolphin/PCSX2 all store a
/// token, never the plaintext password) - endpoint/params/response fields verified against
/// RetroAchievements' own Connect API docs (api-docs.retroachievements.org/connect/standalone),
/// not guessed.</summary>
public static class RetroAchievementsClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public static async Task<RetroAchievementsLoginResult> LoginAsync(string username, string password)
    {
        try
        {
            var url = $"https://retroachievements.org/dorequest.php?r=login2" +
                       $"&u={Uri.EscapeDataString(username)}&p={Uri.EscapeDataString(password)}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // The docs are explicit that dorequest.php must never be called without one.
            request.Headers.UserAgent.ParseAdd("GridShell/1.0.0 (Windows)");

            using var response = await Http.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                return new RetroAchievementsLoginResult(false, null, null);
            }

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
            var root = doc.RootElement;

            var success = root.TryGetProperty("Success", out var s) && s.GetBoolean();
            var token = root.TryGetProperty("Token", out var t) ? t.GetString() : null;
            var user = root.TryGetProperty("User", out var u) ? u.GetString() : null;

            if (!success || string.IsNullOrWhiteSpace(token))
            {
                return new RetroAchievementsLoginResult(false, null, null);
            }

            return new RetroAchievementsLoginResult(true, user ?? username, token);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            return new RetroAchievementsLoginResult(false, null, null);
        }
    }
}
