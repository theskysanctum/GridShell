using GridShell.Core.Tiles;

namespace GridShell.Core.Settings;

public sealed class AppSettings
{
    public AppTheme Theme { get; set; } = AppTheme.Dark;
    public string AccentColor { get; set; } = "#4A90D9";
    public UiFontChoice UiFont { get; set; } = UiFontChoice.Default;
    public string? CustomFontPath { get; set; }
    /// <summary>Currently selected Store Scene path; property name retained for settings-file compatibility.</summary>
    public string? WallpaperUrl { get; set; }
    public bool WallpaperMuted { get; set; }
    public string? StartButtonIconCodepoint { get; set; } = "e9b0";
    public string? StartButtonIconImagePath { get; set; }
    public bool KioskModeEnabled { get; set; }
    public string? KioskModePinHash { get; set; }
    public string? RealDebridApiToken { get; set; }
    public string? DownloadManagerFolder { get; set; }
    public string? DownloadCookies { get; set; }
    public string? GameLinksRepoUrl { get; set; }
    public string? IgdbClientId { get; set; }
    public string? IgdbClientSecret { get; set; }
    public string? TheGamesDbApiKey { get; set; }
    public string? RetroAchievementsUsername { get; set; }
    public string? RetroAchievementsToken { get; set; }
    public bool WinKeyHookEnabled { get; set; } = true;
    public TileSize TileSize { get; set; } = TileSize.Medium;
    public string? DefaultBrowserExecutablePath { get; set; }
    public List<string> ExtraRomPaths { get; set; } = new();
    public Dictionary<string, EmulatorConfig> EmulatorConfigs { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
