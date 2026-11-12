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
