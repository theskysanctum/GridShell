namespace GridShell.Core.Tiles;

/// <summary>One emulator's download page, offered for a platform in Settings' Emulation tab.
/// Links go to the emulator's own official download/platforms page rather than a specific
/// version-pinned installer URL, so they never go stale as new versions release.</summary>
public sealed record EmulatorDownloadLink(string Name, string Url);

/// <summary>Which emulator(s) to offer downloading for each platform. RetroArch alone covers
/// the vast majority of platforms here via its libretro cores (its own in-app Online Updater
/// handles picking/installing the right core), so it's the default for everything; a platform
/// gets a specific override only where a dedicated standalone emulator is the better/expected
/// choice. Deliberately small and verified one link at a time (not attempting all 190
/// platforms at once) - every URL here has been checked against the emulator's own official
/// site, not guessed.</summary>
public static class EmulatorDownloadCatalog
{
    private static readonly EmulatorDownloadLink RetroArch =
        new("RetroArch", "https://www.retroarch.com/?page=platforms");

    private static readonly EmulatorDownloadLink Dolphin =
        new("Dolphin", "https://dolphin-emu.org/download/");

    private static readonly EmulatorDownloadLink Pcsx2 =
        new("PCSX2", "https://pcsx2.net/downloads");

    private static readonly EmulatorDownloadLink Ruffle =
        new("Ruffle", "https://ruffle.rs/downloads");

    private static readonly EmulatorDownloadLink Rpcs3 =
        new("RPCS3", "https://rpcs3.net/download");

    private static readonly EmulatorDownloadLink ShadPs4 =
        new("shadPS4", "https://github.com/shadps4-emu/shadPS4/releases");

    private static readonly EmulatorDownloadLink Cemu =
        new("Cemu", "https://github.com/cemu-project/Cemu/releases");

    private static readonly EmulatorDownloadLink VisualPinball =
        new("Visual Pinball X", "https://github.com/vpinball/vpinball/releases");

    private static readonly EmulatorDownloadLink Vita3K =
        new("Vita3K", "https://vita3k.org/download");

    private static readonly EmulatorDownloadLink Xemu =
        new("Xemu", "https://xemu.app/docs/download");

    private static readonly EmulatorDownloadLink XeniaCanary =
        new("Xenia Canary", "https://github.com/xenia-canary/xenia-canary/releases/latest");

    private static readonly EmulatorDownloadLink Mame =
        new("MAME", "https://github.com/mamedev/mame/releases");

    private static readonly EmulatorDownloadLink Azahar =
        new("Azahar", "https://azahar-emu.org/");

    private static readonly EmulatorDownloadLink MelonDs =
        new("melonDS", "https://github.com/melonDS-emu/melonDS/releases/latest");

    private static readonly EmulatorDownloadLink Ppsspp =
        new("PPSSPP", "https://github.com/hrydgard/ppsspp/releases/latest");

    private static readonly EmulatorDownloadLink MiniVmac =
        new("Mini vMac", "https://www.gryphel.com/c/minivmac/download.html");

    private static readonly EmulatorDownloadLink Supermodel =
        new("Supermodel", "https://github.com/trzy/Supermodel/releases/latest");

    /// <summary>Platforms better served by a dedicated standalone emulator than a RetroArch
    /// core - still offered alongside RetroArch where RetroArch does have a workable core too
    /// (just not usually the best choice); omitted where RetroArch doesn't have one at all
    /// (PS4, Visual Pinball). ScummVM is deliberately NOT listed here - it runs through
    /// RetroArch's own core, so it's left to the plain default below.</summary>
    private static readonly Dictionary<string, EmulatorDownloadLink[]> Overrides = new()
    {
        ["gamecube"] = new[] { Dolphin, RetroArch },
        ["wii"] = new[] { Dolphin, RetroArch },
        ["ps2"] = new[] { Pcsx2, RetroArch },
        ["flash"] = new[] { Ruffle },

        // The "hefty" systems - demanding enough that a dedicated standalone is the only
        // realistic choice, not just the better one.
        ["ps3"] = new[] { Rpcs3 },
        ["ps4"] = new[] { ShadPs4 },
        ["wiiu"] = new[] { Cemu },
        ["vpinball"] = new[] { VisualPinball },
        ["psvita"] = new[] { Vita3K },
        ["xbox"] = new[] { Xemu },
        ["xbox360"] = new[] { XeniaCanary },

        // Standalone MAME tracks current MAME releases directly - RetroArch's bundled MAME
        // cores are often several versions behind, so standalone is the more compatible
        // choice while RetroArch is still offered as a lighter-weight alternative.
        ["mame"] = new[] { Mame, RetroArch },

        // Citra (the long-standing standard) was shut down in 2024; Azahar is its actively
        // maintained community successor. No workable RetroArch 3DS core exists.
        ["3ds"] = new[] { Azahar },

        ["nds"] = new[] { MelonDs, RetroArch },
        ["psp"] = new[] { Ppsspp, RetroArch },

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
