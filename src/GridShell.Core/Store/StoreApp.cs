using System.Text.Json.Serialization;

namespace GridShell.Core.Store;

public sealed class StoreApp
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("wingetId")]
    public string WingetId { get; set; } = "";

    /// <summary>"winget" (default) or "github" - github entries have no WingetId, and are
    /// installed instead by GitHubReleaseInstaller downloading and extracting the latest
    /// release's Windows archive (.zip or .7z) from GithubRepo. Used for emulators with no
    /// winget package but a real downloadable GitHub release (RPCS3's own repo has neither, but
    /// its companion RPCS3/rpcs3-binaries-win repo does - see GitHubReleaseInstaller's
    /// remarks).</summary>
    [JsonPropertyName("installMethod")]
    public string InstallMethod { get; set; } = "winget";

    /// <summary>"owner/repo" - only set when InstallMethod is "github".</summary>
    [JsonPropertyName("githubRepo")]
    public string? GithubRepo { get; set; }

    /// <summary>Only set when InstallMethod is "github" and the repo doesn't actually live on
    /// github.com - e.g. Eden (git.eden-emu.dev), a Forgejo instance exposing the same
    /// GitHub-shaped releases API (/api/v1/repos/{owner}/{repo}/releases/latest, same assets[]
    /// shape with name/browser_download_url) since it was repeatedly pushed off GitHub by Nintendo
    /// DMCA pressure. Defaults to GitHub's own API base when omitted.</summary>
    [JsonPropertyName("apiBaseUrl")]
    public string ApiBaseUrl { get; set; } = "https://api.github.com";

    [JsonPropertyName("category")]
    public string Category { get; set; } = "";

    [JsonPropertyName("description")]
    public string Description { get; set; } = "";

    /// <summary>EmulationPlatform slugs this app should be wired to in Settings'
    /// EmulatorConfigs once installed - empty for anything that isn't an emulator, or an
    /// emulator (like RetroArch) that needs a manual per-core setup step it can't do for you.</summary>
    [JsonPropertyName("platformSlugs")]
    public List<string> PlatformSlugs { get; set; } = new();
}

public sealed class StoreCatalogFile
{
    [JsonPropertyName("apps")]
    public List<StoreApp> Apps { get; set; } = new();
}
