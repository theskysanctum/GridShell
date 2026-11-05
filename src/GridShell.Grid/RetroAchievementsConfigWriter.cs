using System.IO;
using System.Linq;
using GridShell.Core.Settings;

namespace GridShell;

/// <summary>Pushes the logged-in RetroAchievements account into every RetroArch, Dolphin, and
/// PCSX2 install currently configured in Settings' Emulation tab - each one's own config file
/// location and key names verified against its own source/docs, not guessed. All three store a
/// persistent token (never the plaintext password), matching RetroAchievementsClient's own
/// login exchange.</summary>
public static class RetroAchievementsConfigWriter
{
    public static void ApplyToAllConfiguredEmulators(AppSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.RetroAchievementsUsername)
            || string.IsNullOrWhiteSpace(settings.RetroAchievementsToken))
        {
            return;
        }

        var applied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var exePath in settings.EmulatorConfigs.Values
                     .Select(c => c.EmulatorPath)
                     .Where(p => !string.IsNullOrWhiteSpace(p))
                     .Cast<string>())
        {
            var exeName = Path.GetFileNameWithoutExtension(exePath);

            try
            {
                if (exeName.Contains("retroarch", StringComparison.OrdinalIgnoreCase))
                {
                    var dir = Path.GetDirectoryName(exePath) ?? "";
                    if (applied.Add("ra:" + dir))
                    {
                        ApplyToRetroArch(dir, settings);
                    }
                }
                else if (exeName.Contains("dolphin", StringComparison.OrdinalIgnoreCase))
                {
                    var configDir = GetDolphinConfigDir(exePath);
                    if (applied.Add("dolphin:" + configDir))
                    {
                        ApplyToDolphin(configDir, settings);
                    }
                }
                else if (exeName.Contains("pcsx2", StringComparison.OrdinalIgnoreCase))
                {
                    var inisDir = GetPcsx2InisDir(exePath);
                    if (applied.Add("pcsx2:" + inisDir))
                    {
                        ApplyToPcsx2(inisDir, settings);
                    }
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best-effort - a locked/missing config folder for one emulator shouldn't stop
                // applying to the others.
            }
        }
    }

    private static void ApplyToRetroArch(string dir, AppSettings settings)
    {
        var cfgPath = Path.Combine(dir, "retroarch.cfg");

        // cheevos_enable is reliably respected. cheevos_username/cheevos_token are flagged
        // "sensitive" by current RetroArch builds and may get silently stripped the next time
        // RetroArch itself saves retroarch.cfg - written anyway as a best-effort (works on
        // older builds, harmless no-op otherwise). On a build that strips them, the user still
        // needs to log in once via RetroArch's own Quick Menu > Achievements - there's no
        // documented way around that right now.
        UpsertFlatConfigValue(cfgPath, "cheevos_enable", "true");
        UpsertFlatConfigValue(cfgPath, "cheevos_username", settings.RetroAchievementsUsername!);
        UpsertFlatConfigValue(cfgPath, "cheevos_token", settings.RetroAchievementsToken!);
    }

    private static void ApplyToDolphin(string configDir, AppSettings settings)
    {
        var path = Path.Combine(configDir, "RetroAchievements.ini");
        UpsertIniValue(path, "Achievements", "Enabled", "True");
        UpsertIniValue(path, "Achievements", "Username", settings.RetroAchievementsUsername!);
        UpsertIniValue(path, "Achievements", "ApiToken", settings.RetroAchievementsToken!);
    }

    private static void ApplyToPcsx2(string inisDir, AppSettings settings)
    {
        // PCSX2 deliberately splits these across two files - the token specifically goes in
        // secrets.ini, not the base PCSX2.ini, confirmed from PCSX2's own source.
        UpsertIniValue(Path.Combine(inisDir, "PCSX2.ini"), "Achievements", "Username", settings.RetroAchievementsUsername!);
        UpsertIniValue(Path.Combine(inisDir, "secrets.ini"), "Achievements", "Token", settings.RetroAchievementsToken!);
    }

    private static string GetDolphinConfigDir(string dolphinExePath)
    {
        var exeDir = Path.GetDirectoryName(dolphinExePath) ?? "";
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
        {
            return Path.Combine(exeDir, "User", "Config");
        }

        var documentsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "Dolphin Emulator", "Config");
        if (Directory.Exists(documentsDir))
        {
            return documentsDir;
        }

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Dolphin Emulator", "Config");
    }

    private static string GetPcsx2InisDir(string pcsx2ExePath)
    {
        var exeDir = Path.GetDirectoryName(pcsx2ExePath) ?? "";
        if (File.Exists(Path.Combine(exeDir, "portable.txt")))
        {
            return Path.Combine(exeDir, "inis");
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "PCSX2", "inis");
    }

    /// <summary>RetroArch's config format - no sections, just "key = \"value\"" per line.</summary>
    private static void UpsertFlatConfigValue(string filePath, string key, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var lines = File.Exists(filePath) ? File.ReadAllLines(filePath).ToList() : new List<string>();

        var lineIndex = lines.FindIndex(l => IsKeyLine(l, key));
        var newLine = $"{key} = \"{value}\"";

        if (lineIndex >= 0)
        {
            lines[lineIndex] = newLine;
        }
        else
        {
            lines.Add(newLine);
        }

        File.WriteAllLines(filePath, lines);
    }

    /// <summary>Dolphin/PCSX2's config format - "[Section]" headers with "key = value" lines
    /// underneath. Creates the section (and file) if missing, otherwise updates the key in
    /// place or inserts it into the existing section.</summary>
    private static void UpsertIniValue(string filePath, string section, string key, string value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
        var lines = File.Exists(filePath) ? File.ReadAllLines(filePath).ToList() : new List<string>();

        var sectionHeader = $"[{section}]";
        var sectionIndex = lines.FindIndex(l => l.Trim().Equals(sectionHeader, StringComparison.OrdinalIgnoreCase));

        if (sectionIndex == -1)
        {
            if (lines.Count > 0 && lines[^1].Trim().Length > 0)
            {
                lines.Add("");
            }

            lines.Add(sectionHeader);
            lines.Add($"{key} = {value}");
            File.WriteAllLines(filePath, lines);
            return;
        }

        var sectionEnd = lines.FindIndex(sectionIndex + 1, l => l.TrimStart().StartsWith('['));
        if (sectionEnd == -1)
        {
            sectionEnd = lines.Count;
        }

        var keyIndex = -1;
        for (var i = sectionIndex + 1; i < sectionEnd; i++)
        {
            if (IsKeyLine(lines[i], key))
            {
                keyIndex = i;
                break;
            }
        }

        if (keyIndex >= 0)
        {
            lines[keyIndex] = $"{key} = {value}";
        }
        else
        {
            lines.Insert(sectionIndex + 1, $"{key} = {value}");
        }

        File.WriteAllLines(filePath, lines);
    }

    private static bool IsKeyLine(string line, string key)
    {
        var trimmed = line.TrimStart();
        return trimmed.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith(key + "=", StringComparison.OrdinalIgnoreCase);
    }
}
