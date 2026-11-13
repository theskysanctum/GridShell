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

    /// <summary>"winget" (default), "github", "package", "direct", or "browser" - github
    /// entries have no WingetId, and are installed instead by GitHubReleaseInstaller downloading
    /// and extracting the latest release's Windows archive (.zip or .7z) from GithubRepo. Used
    /// for emulators with no winget package but a real downloadable GitHub release (RPCS3's own
    /// repo has neither, but its companion RPCS3/rpcs3-binaries-win repo does - see
    /// GitHubReleaseInstaller's remarks). "package" is for one that has neither (e.g. Dolphin -
    /// winget's own package is broken, and Dolphin publishes no GitHub releases at all) -
    /// PackageInstaller extracts PackageFile straight out of this same synced repo clone
    /// instead. "direct" is for apps that winget CAN install but whose own registry entry
    /// breaks under winget's quirks (see DirectInstaller's remarks) - WingetId is reused as the
    /// winget-pkgs PackageIdentifier purely to look up the current installer URL/type/switches,
    /// but the actual install/uninstall never goes through winget.exe itself. "browser" (Steam,
    /// Heroic, itch) is the simplest of all: after enough trouble automating these specific
    /// installs, the Store's Install button just opens BrowserUrl and leaves the actual
    /// install/uninstall entirely to the user - GridShell still detects whether it's really
    /// installed afterward via the normal registry/exe lookup, same as every other method.</summary>
    [JsonPropertyName("installMethod")]
    public string InstallMethod { get; set; } = "winget";

    /// <summary>The official download/info page to open - only set when InstallMethod is
    /// "browser".</summary>
    [JsonPropertyName("browserUrl")]
    public string? BrowserUrl { get; set; }

    /// <summary>Only meaningful for InstallMethod "winget" - Steam's own winget package only
    /// offers a Scope:machine install with a genuine elevation requirement (confirmed live:
    /// its real installer refuses to even start non-elevated, not just a UAC prompt), unlike
    /// every other winget-installed catalog app. WingetInstaller runs install/uninstall for
    /// this one app via Windows' own "runas" verb - a single UAC prompt for just that one
    /// winget.exe invocation, not GridShell itself running elevated. Also skips --location for
    /// the same app (confirmed separately: a custom --location breaks Steam's own registry
    /// entry regardless of elevation).</summary>
    [JsonPropertyName("requiresElevation")]
    public bool RequiresElevation { get; set; }

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

    /// <summary>Filename under store-catalog/packages/ in the repo - only set when
    /// InstallMethod is "package".</summary>
    [JsonPropertyName("packageFile")]
    public string? PackageFile { get; set; }

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
