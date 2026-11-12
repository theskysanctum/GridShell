using System.IO;
using GridShell.Core.Tiles;

namespace GridShell.Core.Store;

/// <summary>Same spirit as RetroArchCoreAutoWire's SetSystemDirectory, for the standalone
/// emulators that have their own settable BIOS/firmware directory - called right after
/// EmulatorAutoWire resolves an app's exe path. Pre-creates a per-emulator subfolder under
/// EmulationPlatforms.BiosRootPath (e.g. Bios\PCSX2\) and points that emulator's own config file
/// at it, same convention as the RetroArch subfolder. The user still has to drop their own
/// legally-obtained BIOS/firmware files in there - this only wires the path, same as RetroArch's
/// system_directory wiring never supplies the files themselves.
///
/// Deliberately NOT handled here (confirmed during research, not guessed): Dolphin and Xenia need
/// no BIOS/firmware at all for normal operation. Cemu, Azahar, Vita3K, RPCS3, Supermodel, and
/// xemu either have no single settable "directory" (file-presence or GUI-installer driven
/// instead) or their exact config key/location couldn't be confirmed against an authoritative
/// source - wiring those with guessed keys would silently fail rather than error, which is worse
/// than leaving them unconfigured.</summary>
public static class EmulatorBiosAutoWire
{
    public static void TryWire(string appId, string exePath)
    {
        switch (appId)
        {
            case "pcsx2team.pcsx2":
                WirePcsx2(exePath);
                break;
            case "stenzek.duckstation":
                WireDuckStation(exePath);
                break;
            case "melonds.melonds":
                WireMelonDs(exePath);
                break;
            case "mamedev.mame":
                WireMame(exePath);
                break;
        }
    }

    private static string CreateSubfolder(string name)
    {
        var dir = Path.Combine(EmulationPlatforms.BiosRootPath, name);
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>PCSX2's Qt build keys its BIOS search directory as [Folders] Bios= in
    /// PCSX2.ini. Non-portable (winget default) installs keep that file under
    /// Documents\PCSX2\inis\PCSX2.ini; dropping an empty portable.ini next to the exe switches it
    /// to inis\PCSX2.ini next to the exe instead - detected here rather than assumed, since
    /// winget's default is non-portable.</summary>
    private static void WirePcsx2(string exePath)
    {
        var biosDir = CreateSubfolder("PCSX2");
        var exeDir = Path.GetDirectoryName(exePath) ?? "";
        var isPortable = File.Exists(Path.Combine(exeDir, "portable.ini"));
        var inisDir = isPortable
            ? Path.Combine(exeDir, "inis")
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PCSX2", "inis");

        Directory.CreateDirectory(inisDir);
        UpdateIniKey(Path.Combine(inisDir, "PCSX2.ini"), "Folders", "Bios", biosDir);
    }

    /// <summary>DuckStation keys its BIOS search directory as [BIOS] SearchDirectory= in
    /// settings.ini. Non-portable (winget default) installs keep it under
    /// Documents\DuckStation\settings.ini; an empty portable.txt next to the exe switches it to
    /// settings.ini next to the exe instead.</summary>
    private static void WireDuckStation(string exePath)
    {
        var biosDir = CreateSubfolder("DuckStation");
        var exeDir = Path.GetDirectoryName(exePath) ?? "";
        var isPortable = File.Exists(Path.Combine(exeDir, "portable.txt"));
        var settingsDir = isPortable
            ? exeDir
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "DuckStation");

        Directory.CreateDirectory(settingsDir);
        UpdateIniKey(Path.Combine(settingsDir, "settings.ini"), "BIOS", "SearchDirectory", biosDir);
    }

    /// <summary>melonDS has no BIOS search *directory* - DS.BIOS9Path/DS.BIOS7Path/
    /// DS.FirmwarePath each point at one specific file, TOML-format in melonDS.toml next to the
    /// exe (portable-by-default). Points all three at the per-emulator subfolder with the
    /// filenames melonDS's own docs use - the files themselves still need to be dropped there by
    /// the user, same as RetroArch's system_directory never supplies the actual cores/BIOS
    /// either, just the path.</summary>
    private static void WireMelonDs(string exePath)
    {
        var biosDir = CreateSubfolder("melonDS");
        var exeDir = Path.GetDirectoryName(exePath) ?? "";
        var tomlPath = Path.Combine(exeDir, "melonDS.toml");

        UpdateTomlKey(tomlPath, "DS.BIOS9Path", Path.Combine(biosDir, "bios9.bin"));
        UpdateTomlKey(tomlPath, "DS.BIOS7Path", Path.Combine(biosDir, "bios7.bin"));
        UpdateTomlKey(tomlPath, "DS.FirmwarePath", Path.Combine(biosDir, "firmware.bin"));
    }

    /// <summary>Standalone MAME has no separate BIOS directory - arcade BIOS sets are searched
    /// for in the same rompath list as regular ROMs (mame.ini, portable-by-default next to
    /// mame.exe). Appends the shared ROMs\mame folder and a dedicated Bios\MAME folder to
    /// whatever rompath is already there rather than overwriting it, since the user may already
    /// have their own paths configured.</summary>
    private static void WireMame(string exePath)
    {
        var biosDir = CreateSubfolder("MAME");
        var romsDir = Path.Combine(EmulationPlatforms.RomsRootPath, "mame");
        Directory.CreateDirectory(romsDir);

        var exeDir = Path.GetDirectoryName(exePath) ?? "";
        var iniPath = Path.Combine(exeDir, "mame.ini");

        var existingPaths = new List<string>();
        string[] lines;
        if (File.Exists(iniPath))
        {
            lines = File.ReadAllLines(iniPath);
            var lineIndex = Array.FindIndex(lines, l => l.TrimStart().StartsWith("rompath", StringComparison.OrdinalIgnoreCase));
            if (lineIndex >= 0)
            {
                var value = lines[lineIndex]["rompath".Length..].Trim();
                existingPaths.AddRange(value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
            }
        }
        else
        {
            lines = Array.Empty<string>();
        }

        foreach (var required in new[] { romsDir, biosDir })
        {
            if (!existingPaths.Contains(required, StringComparer.OrdinalIgnoreCase))
            {
                existingPaths.Add(required);
            }
        }

        var desiredLine = $"rompath {string.Join(';', existingPaths)}";
        var replaceIndex = Array.FindIndex(lines, l => l.TrimStart().StartsWith("rompath", StringComparison.OrdinalIgnoreCase));

        if (replaceIndex < 0)
        {
            File.WriteAllLines(iniPath, lines.Append(desiredLine));
        }
        else if (lines[replaceIndex] != desiredLine)
        {
            lines[replaceIndex] = desiredLine;
            File.WriteAllLines(iniPath, lines);
        }
    }

    private static void UpdateIniKey(string iniPath, string section, string key, string value)
    {
        var desiredLine = $"{key} = {value}";
        var sectionHeader = $"[{section}]";

        if (!File.Exists(iniPath))
        {
            File.WriteAllLines(iniPath, new[] { sectionHeader, desiredLine });
            return;
        }

        var lines = File.ReadAllLines(iniPath).ToList();
        var sectionIndex = lines.FindIndex(l => l.Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));

        if (sectionIndex < 0)
        {
            lines.Add(sectionHeader);
            lines.Add(desiredLine);
            File.WriteAllLines(iniPath, lines);
            return;
        }

        var sectionEnd = lines.FindIndex(sectionIndex + 1, l => l.TrimStart().StartsWith('['));
        if (sectionEnd < 0)
        {
            sectionEnd = lines.Count;
        }

        var keyIndex = lines.FindIndex(sectionIndex + 1, sectionEnd - sectionIndex - 1,
            l => l.TrimStart().StartsWith(key + " ", StringComparison.OrdinalIgnoreCase)
                || l.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));

        if (keyIndex < 0)
        {
            lines.Insert(sectionEnd, desiredLine);
        }
        else if (lines[keyIndex] != desiredLine)
        {
            lines[keyIndex] = desiredLine;
        }
        else
        {
            return;
        }

        File.WriteAllLines(iniPath, lines);
    }

    private static void UpdateTomlKey(string tomlPath, string dottedKey, string value)
    {
        var desiredLine = $"{dottedKey} = \"{value.Replace("\\", "\\\\")}\"";

        if (!File.Exists(tomlPath))
        {
            File.WriteAllLines(tomlPath, new[] { desiredLine });
            return;
        }

        var lines = File.ReadAllLines(tomlPath).ToList();
        var keyIndex = lines.FindIndex(l => l.TrimStart().StartsWith(dottedKey + " ", StringComparison.OrdinalIgnoreCase)
            || l.TrimStart().StartsWith(dottedKey + "=", StringComparison.OrdinalIgnoreCase));

        if (keyIndex < 0)
        {
            lines.Add(desiredLine);
        }
        else if (lines[keyIndex] != desiredLine)
        {
            lines[keyIndex] = desiredLine;
        }
        else
        {
            return;
        }

        File.WriteAllLines(tomlPath, lines);
    }
}
