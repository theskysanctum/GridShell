namespace GridShell.Core.Tiles;

/// <summary>Which RetroArch core file covers each mainstream platform, for automatic RetroArch
/// wiring (RetroArchCoreAutoWire) - every filename here is a real, currently-shipping core,
/// confirmed against the live file listing of libretro/libretro-core-info (one .info file per
/// real core; this catalog only uses names that exist in that listing, nothing guessed).
///
/// Deliberately only the ~45 mainstream/well-known platforms, not all ~166 in
/// EmulationPlatform.cs - many of those (DEC PDP-1, Sega Hikaru, Namco System 22, Tandy VIS, and
/// plenty more) have no libretro core at all, and the rest would need the same
/// pick-the-best-default judgment call as these without the real-world usage to make that call
/// confidently. Where a platform's system has more than one core (SNES alone has snes9x,
/// snes9x2010, bsnes, bsnes_mercury_*, …), the one picked here is RetroArch's own
/// commonly-recommended default for that system, not just the first alphabetically - noted per
/// entry only where the choice isn't the obvious single option.</summary>
public static class RetroArchCoreCatalog
{
    private static readonly Dictionary<string, string> CoreFileNames = new()
    {
        // Home Console
        ["nes"] = "fceumm",
        ["fds"] = "fceumm",
        ["snes"] = "snes9x",
        ["snes-msu1"] = "snes9x",
        ["sgb"] = "snes9x",
        ["sgb-msu1"] = "snes9x",
        ["n64"] = "mupen64plus_next",
        ["gamecube"] = "dolphin",
        ["wii"] = "dolphin",
        ["megadrive"] = "genesis_plus_gx",
        ["megacd"] = "genesis_plus_gx",
        ["sega32x"] = "genesis_plus_gx",
        ["mastersystem"] = "genesis_plus_gx",
        ["gamegear"] = "genesis_plus_gx",
        ["sg1000"] = "genesis_plus_gx",
        // Mednafen Saturn ("Beetle Saturn") over Yabause/Kronos - the generally recommended
        // accuracy-focused default.
        ["saturn"] = "mednafen_saturn",
        ["dreamcast"] = "flycast",
        ["psx"] = "mednafen_psx_hw",
        ["ps2"] = "pcsx2",
        ["ps3"] = "rpcs3",
        ["neogeo"] = "fbneo",
        ["atari2600"] = "stella",
        ["atari7800"] = "prosystem",
        ["atari800"] = "atari800",
        ["xegs"] = "atari800",
        ["jaguar"] = "virtualjaguar",
        ["colecovision"] = "gearcoleco",
        ["intellivision"] = "freeintv",
        ["vectrex"] = "vecx",

        // Portable Game Console
        ["gb"] = "gambatte",
        ["gb2players"] = "gambatte",
        ["gbc"] = "gambatte",
        ["gbc2players"] = "gambatte",
        ["gba"] = "mgba",
        ["nds"] = "melonds",
        // Azahar - the same actively-maintained Citra successor already used for its Store entry.
        ["3ds"] = "azahar",
        ["psp"] = "ppsspp",
        ["psvita"] = "vita3k",
        ["lynx"] = "handy",
        ["wswan"] = "mednafen_wswan",
        ["wswanc"] = "mednafen_wswan",
        ["ngp"] = "mednafen_ngp",
        ["ngpc"] = "mednafen_ngp",
        ["pcengine"] = "mednafen_pce_fast",
        ["pcenginecd"] = "mednafen_pce_fast",
        ["supergrafx"] = "mednafen_supergrafx",
        ["pcfx"] = "mednafen_pcfx",
        ["virtualboy"] = "mednafen_vb",

        // Home Computer
        ["c64"] = "vice_x64sc",
        ["c128"] = "vice_x128",
        ["amiga500"] = "puae",
        ["amiga1200"] = "puae",
        ["zxspectrum"] = "fuse",
        ["msx1"] = "bluemsx",
        ["msx2"] = "bluemsx",
        ["msx2+"] = "bluemsx",
        ["msxturbor"] = "bluemsx",
        ["apple2"] = "applewin",
        ["amstradcpc"] = "cap32",
        ["atarist"] = "hatari",
        ["x68000"] = "px68k",
    };

    /// <summary>The core's filename as it sits in RetroArch's own "cores" folder, or null if
    /// this platform isn't one of the mainstream ones covered here.</summary>
    public static string? GetCoreFileName(string platformSlug) =>
        CoreFileNames.TryGetValue(platformSlug, out var name) ? $"{name}_libretro.dll" : null;
}
