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

    private static readonly EmulatorDownloadLink Rpcs3 =
        new("RPCS3", "https://rpcs3.net/download");

    private static readonly EmulatorDownloadLink VisualPinball =
        new("Visual Pinball X", "https://github.com/vpinball/vpinball/releases");

    private static readonly EmulatorDownloadLink MiniVmac =
        new("Mini vMac", "https://www.gryphel.com/c/minivmac/download.html");

    private static readonly EmulatorDownloadLink Supermodel =
        new("Supermodel", "https://github.com/trzy/Supermodel/releases/latest");

    /// <summary>Platforms whose best/only realistic emulator has no winget package, so a direct
    /// download link is still the only way to get it - everything that DOES have a winget
    /// package (Dolphin, PCSX2, Cemu, Vita3K, xemu, Xenia, MAME, Azahar, melonDS, PPSSPP,
    /// shadPS4, Ruffle) now lives in the Store instead, confirmed via `winget search` for each.
    /// RPCS3 has neither a winget package nor any downloadable GitHub release asset (confirmed:
    /// its own release page has zero attached assets and points users to rpcs3.net instead).</summary>
    private static readonly Dictionary<string, EmulatorDownloadLink[]> Overrides = new()
    {
        ["ps3"] = new[] { Rpcs3 },
        ["vpinball"] = new[] { VisualPinball },

        // Mini vMac over Basilisk II - more accurate (emulates real 68k hardware rather than
        // patching ROMs) and still actively maintained; Basilisk II effectively isn't anymore.
        // No RetroArch core covers classic Mac.
        ["macintosh"] = new[] { MiniVmac },

        // Sega Model 3 - no realistic RetroArch/MAME coverage; Supermodel is the standard.
        // Model 2 is deliberately NOT listed here - its only known emulator (Sega Model 2
        // Emulator / ElSemi's) has no official download source anymore (site defunct, never
        // had an official GitHub release), so there's nothing verifiable to link to.
        ["model3"] = new[] { Supermodel },
    };

    public static IReadOnlyList<EmulatorDownloadLink> GetLinksFor(string platformSlug) =>
        Overrides.TryGetValue(platformSlug, out var specific) ? specific : new[] { RetroArch };
}
