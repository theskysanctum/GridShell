using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;

namespace GridShell;

/// <summary>Extract/compress actions for the File Explorer, driven through the bundled 7za.exe
/// (Tools\7za.exe, 7-Zip's own standalone console build) rather than detecting whatever archiver
/// the user happens to have installed - verified live to run completely standalone (no companion
/// DLL) and extract/create both .7z and .zip correctly. Covers tar/gzip/bzip2/xz extraction too;
/// RAR is the one format it can't handle (needs separately-licensed unrar code this build
/// doesn't include), so .rar isn't in ArchiveExtensions.</summary>
public static class ArchiveOperations
{
    private static readonly string[] ArchiveExtensions =
    {
        ".zip", ".7z", ".tar", ".gz", ".bz2", ".xz",
    };

    private static string ToolPath => Path.Combine(AppContext.BaseDirectory, "Tools", "7za.exe");

    public static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool ExtractHere(string archivePath, Window owner)
    {
        var containingDir = Path.GetDirectoryName(archivePath) ?? ".";
        return Run($"x \"{archivePath}\" -o\"{containingDir}\" -y", containingDir, owner);
    }

    public static bool ExtractTo(string archivePath, string destinationFolder, Window owner) =>
        Run($"x \"{archivePath}\" -o\"{destinationFolder}\" -y", null, owner);

    public static bool AddToArchive(string sourcePath, bool asSevenZip, Window owner)
    {
        var containingDir = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar)) ?? ".";
        var baseName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
        var extension = asSevenZip ? ".7z" : ".zip";
        var archivePath = Path.Combine(containingDir, baseName + extension);

        return Run($"a \"{archivePath}\" \"{baseName}\"", containingDir, owner);
    }

    private static bool Run(string args, string? workingDirectory, Window owner)
    {
        if (!File.Exists(ToolPath))
        {
            new ToastWindow("7za.exe is missing from this install.", TimeSpan.FromSeconds(5), isWarning: true).Show();
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo(ToolPath, args) { UseShellExecute = false };
            if (workingDirectory is not null)
            {
                psi.WorkingDirectory = workingDirectory;
            }

            using var process = Process.Start(psi);
            process?.WaitForExit();
            return true;
        }
        catch (Exception ex) when (ex is IOException or Win32Exception)
        {
            new ToastWindow($"Archive operation failed: {ex.Message}", TimeSpan.FromSeconds(5), isWarning: true).Show();
            return false;
        }
    }
}
