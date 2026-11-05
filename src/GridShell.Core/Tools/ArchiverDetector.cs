using System.IO;
using System.Security;
using Microsoft.Win32;

namespace GridShell.Core.Tools;

public enum ArchiverKind
{
    SevenZip,
    PeaZip,
}

public record DetectedArchiver(ArchiverKind Kind, string ExecutablePath);

/// <summary>Finds an installed archiver to drive from the File Explorer's Extract/Compress
/// actions - prefers 7-Zip (its CLI syntax is solidly documented) and falls back to PeaZip.</summary>
public static class ArchiverDetector
{
    public static DetectedArchiver? DetectPreferred() => DetectSevenZip() ?? DetectPeaZip();

    private static DetectedArchiver? DetectSevenZip()
    {
        foreach (var subKey in new[] { @"SOFTWARE\7-Zip", @"SOFTWARE\WOW6432Node\7-Zip" })
        {
            try
            {
                using var key = Registry.LocalMachine.OpenSubKey(subKey);
                var installDir = key?.GetValue("Path") as string;
                if (string.IsNullOrWhiteSpace(installDir))
                {
                    continue;
                }

                // 7zG.exe is the GUI wrapper around the same CLI - shows a native progress
                // dialog instead of a blocking console window, for the identical arguments.
                var gui = Path.Combine(installDir, "7zG.exe");
                if (File.Exists(gui))
                {
                    return new DetectedArchiver(ArchiverKind.SevenZip, gui);
                }

                var cli = Path.Combine(installDir, "7z.exe");
                if (File.Exists(cli))
                {
                    return new DetectedArchiver(ArchiverKind.SevenZip, cli);
                }
            }
            catch (SecurityException)
            {
            }
        }

        // No registry key (e.g. a portable install) - fall back to the standard install path.
        var fallback = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7zG.exe");
        return File.Exists(fallback) ? new DetectedArchiver(ArchiverKind.SevenZip, fallback) : null;
    }

    private static DetectedArchiver? DetectPeaZip()
    {
        // PeaZip's registry footprint is inconsistent across installers (Inno Setup vs.
        // Chocolatey vs. portable), so this checks the well-known install paths directly
        // rather than relying on any single registry key.
        var candidates = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "PeaZip", "peazip.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "PeaZip", "peazip.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "PeaZip", "peazip.exe"),
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return new DetectedArchiver(ArchiverKind.PeaZip, candidate);
            }
        }

        return null;
    }
}
