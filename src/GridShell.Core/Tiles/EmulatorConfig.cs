namespace GridShell.Core.Tiles;

/// <summary>The emulator GridShell launches for one platform, and how to pass it a ROM.
/// ArgumentTemplate's "{rom}" is replaced with the (quoted) ROM path at launch time — the
/// default of just the ROM path works for most standalone emulators (Snes9x, Dolphin,
/// DuckStation, …); a frontend like RetroArch needs a fuller template, e.g.
/// -L "cores\snes9x_libretro.dll" "{rom}".</summary>
public sealed class EmulatorConfig
{
    public string? EmulatorPath { get; set; }
    public string ArgumentTemplate { get; set; } = "\"{rom}\"";
}
