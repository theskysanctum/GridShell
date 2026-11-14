using GridShell.Core.Tiles;

namespace GridShell.Core.Settings;

public class AppSettings
{
    public string? DefaultBrowserExecutablePath { get; set; }
    public TileSize TileSize { get; set; } = TileSize.Medium;
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public string AccentColor { get; set; } = "#4A90D9";
    public bool WinKeyHookEnabled { get; set; } = true;

    /// <summary>Which bundled font the whole UI (both processes) renders with - Default
    /// (Roboto) or Gaming (Exo 2). Applied live via an app-level DynamicResource, same as
    /// AccentColor below.</summary>
    public UiFontChoice UiFont { get; set; } = UiFontChoice.Gaming;

    /// <summary>A .ttf/.otf file the user picked, used when UiFont is Custom.</summary>
    public string? CustomFontPath { get; set; }

    /// <summary>The taskbar launcher ("Start") button's icon - a Material Symbols codepoint
    /// (hex string, e.g. "e5c3") picked from the curated gallery in Settings' Appearance tab.
    /// Ignored when StartButtonIconImagePath is set.</summary>
    public string StartButtonIconCodepoint { get; set; } = "e5c3";

    /// <summary>When set, overrides StartButtonIconCodepoint with a user-picked image file
    /// instead of a glyph from the icon font.</summary>
    public string? StartButtonIconImagePath { get; set; }

    /// <summary>When set, overrides the default (the current Windows desktop wallpaper image)
    /// as the content WallpaperWindow's WebView2 renders — a local .html file path or any
    /// http(s) URL, so the "wallpaper" can be live/animated web content instead of a static
    /// image.</summary>
    public string? WallpaperUrl { get; set; }

    /// <summary>Muted by default since an autoplaying video wallpaper with sound is exactly the
    /// kind of thing nobody asked for — applied via CoreWebView2.IsMuted, not a page-level
    /// attribute, so it affects whatever content WallpaperUrl points at.</summary>
    public bool WallpaperMuted { get; set; } = true;
    public string? IgdbClientId { get; set; }
    public string? IgdbClientSecret { get; set; }

    /// <summary>Real-Debrid's private API token (from real-debrid.com/apitoken) - used by the
    /// download manager to unrestrict hoster links and manage torrents.</summary>
    public string? RealDebridApiToken { get; set; }

    /// <summary>Raw HTTP cookie header value for downloads that require login/session cookies,
    /// such as archive.org lending links. Paste the cookie string exactly as browser devtools
    /// show it, e.g. "logged-in-sig=...; PHPSESSID=..."; the app applies it as a Cookie header
    /// for both the direct download path and aria2's RPC addUri call.</summary>
    public string? DownloadCookies { get; set; }

    /// <summary>Where the download manager saves finished downloads - defaults to the user's
    /// Windows Downloads folder when null.</summary>
    public string? DownloadManagerFolder { get; set; }

    /// <summary>Keyed by EmulationPlatform.Slug.</summary>
    public Dictionary<string, EmulatorConfig> EmulatorConfigs { get; set; } = new();

    /// <summary>Logged-in RetroAchievements account - the password itself is never stored,
    /// only the persistent API token the login exchange returns (the same thing
    /// RetroArch/Dolphin/PCSX2 themselves store). Applied to every configured RetroArch,
    /// Dolphin, and PCSX2 install's own config once set.</summary>
    public string? RetroAchievementsUsername { get; set; }
    public string? RetroAchievementsToken { get; set; }

    /// <summary>Additional root folders scanned the same way as <see
    /// cref="EmulationPlatforms.RomsRootPath"/> (each needs its own &lt;platform-slug&gt;
    /// subfolder) — lets ROMs live across multiple drives/locations instead of just the one
    /// default root.</summary>
    public List<string> ExtraRomPaths { get; set; } = new();

    /// <summary>A git repo (any host LibGit2Sharp can clone - "owner/repo" on GitHub, or a full
    /// URL) with one &lt;platform-slug&gt;.json file per EmulationPlatform sitting right at its
    /// root - each a JSON array of [name, url, sizeDisplay] entries - see GameLinksRepoSync.
    /// Lets the Store's "Games" category browse and download real game files, same
    /// shallow-clone-on-open pattern StoreCatalogSync already uses for the app catalog, just
    /// pointed at a repo the user picks rather than GridShell's own.</summary>
    public string? GameLinksRepoUrl { get; set; }

    /// <summary>Locks down editing tiles/wallpaper-appearance and exiting GridShell
    /// (Settings' Exit button, the power menu) - a PIN set via KioskPinWindow, hashed by
    /// PinHasher, is required to turn this back off. Intended for handing the PC to a kid
    /// without worrying about things getting changed or GridShell itself gets closed.</summary>
    public bool KioskModeEnabled { get; set; }

    /// <summary>"&lt;saltBase64&gt;:&lt;hashBase64&gt;" via PinHasher - null whenever
    /// KioskModeEnabled is false (cleared on disable, so re-enabling always requires a fresh
    /// PIN rather than silently reusing an old one).</summary>
    public string? KioskModePinHash { get; set; }
}
