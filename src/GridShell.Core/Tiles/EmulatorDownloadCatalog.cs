namespace GridShell.Core.Tiles;

/// <summary>One emulator's download page, offered for a platform in Settings' Emulation tab.
/// Links go to the emulator's own official download/platforms page rather than a specific
/// version-pinned installer URL, so they never go stale as new versions release.</summary>
public sealed record EmulatorDownloadLink(string Name, string Url);

/// <summary>Which emulator(s) to offer downloading for each platform. RetroArch alone covers
/// the vast majority of platforms here via its libretro cores (its own in-app Online Updater
/// handles picking/installing the right core), so it's the default for everything; a platform
/// gets a specific override only where a dedicated standalone emulator is the better/expected
/// choice AND that emulator has no winget package - anything winget can install is offered
/// through the Store instead (see store-catalog/catalog.json), which also auto-wires
/// EmulatorConfigs on install (EmulatorAutoWire), so a manual download link here would just be
/// a redundant, worse path for those. Checked one emulator at a time against `winget search`
/// and the emulator's own official site, not guessed.</summary>
public static class EmulatorDownloadCatalog
{
    private static readonly EmulatorDownloadLink RetroArch =
        new("RetroArch", "https://www.retroarch.com/?page=platforms");

    /// <summary>Platforms whose best/only realistic emulator has neither a winget package nor a
    /// real GitHub release to install from, so a direct download link would still be the only
    /// way to get it - currently empty, since every platform that previously needed one (Dolphin,
    /// PCSX2, Cemu, Vita3K, xemu, Xenia, MAME, Azahar, melonDS, PPSSPP, shadPS4, Ruffle via
    /// winget; Visual Pinball X, Supermodel, and RPCS3 via GitHubReleaseInstaller) now lives in
    /// the Store instead (store-catalog/catalog.json), confirmed one at a time via
    /// `winget search` and the GitHub releases API. RPCS3's own RPCS3/rpcs3 repo has zero release
    /// assets, but its companion RPCS3/rpcs3-binaries-win repo publishes real, continuously
    /// updated Windows builds via its own CI - confirmed live (asset created the same day it was
    /// checked).</summary>
    private static readonly Dictionary<string, EmulatorDownloadLink[]> Overrides = new();

    public static IReadOnlyList<EmulatorDownloadLink> GetLinksFor(string platformSlug) =>
        Overrides.TryGetValue(platformSlug, out var specific) ? specific : new[] { RetroArch };
}
