using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using GridShell.Core.Tools;

namespace GridShell;

/// <summary>Extract/compress actions for the File Explorer, driven through whichever archiver
/// ArchiverDetector finds installed (7-Zip preferred, PeaZip as a fallback) rather than
/// reimplementing archive formats directly.</summary>
public static class ArchiveOperations
{
    private static readonly string[] ArchiveExtensions =
    {
        ".zip", ".7z", ".rar", ".tar", ".gz", ".bz2", ".xz",
    };

    public static bool IsArchive(string path) =>
        ArchiveExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool ExtractHere(string archivePath, Window owner)
    {
        if (ArchiverDetector.DetectPreferred() is not { } archiver)
        {
            WarnNoArchiver(owner, "extract");
            return false;
        }

        var containingDir = Path.GetDirectoryName(archivePath) ?? ".";
        var args = archiver.Kind == ArchiverKind.SevenZip
            ? $"x \"{archivePath}\" -o\"{containingDir}\" -y"
            : $"-ext2here \"{archivePath}\"";

        return Run(archiver.ExecutablePath, args, containingDir, owner);
    }

    public static bool ExtractTo(string archivePath, string destinationFolder, Window owner)
    {
        if (ArchiverDetector.DetectPreferred() is not { } archiver)
        {
            WarnNoArchiver(owner, "extract");
            return false;
        }

        if (archiver.Kind != ArchiverKind.SevenZip)
        {
            // PeaZip's CLI argument order for "extract to a chosen folder" is inconsistently
            // documented across PeaZip's own pages - rather than risk extracting to the wrong
            // place, fall back to the unambiguous Extract Here when only PeaZip is available.
            MessageBox.Show(owner,
                "Extracting to a chosen folder needs 7-Zip. Extracting here instead.",
                "Extract", MessageBoxButton.OK, MessageBoxImage.Information);
            return ExtractHere(archivePath, owner);
        }

        var args = $"x \"{archivePath}\" -o\"{destinationFolder}\" -y";
        return Run(archiver.ExecutablePath, args, null, owner);
    }

    public static bool AddToArchive(string sourcePath, bool asSevenZip, Window owner)
    {
        if (ArchiverDetector.DetectPreferred() is not { } archiver)
        {
            WarnNoArchiver(owner, "compress");
            return false;
        }

        var containingDir = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar)) ?? ".";
        var baseName = Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar));
        var extension = asSevenZip ? ".7z" : ".zip";
        var archivePath = Path.Combine(containingDir, baseName + extension);

        var args = archiver.Kind == ArchiverKind.SevenZip
            ? $"a \"{archivePath}\" \"{baseName}\""
            : $"{(asSevenZip ? "-add27z" : "-add2zip")} \"{archivePath}\" \"{baseName}\"";

        return Run(archiver.ExecutablePath, args, containingDir, owner);
    }

    private static void WarnNoArchiver(Window owner, string verb) =>
        MessageBox.Show(owner, $"No archiver found - install 7-Zip or PeaZip to {verb} archives.",
            "Archive", MessageBoxButton.OK, MessageBoxImage.Warning);

    private static bool Run(string exePath, string args, string? workingDirectory, Window owner)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath, args) { UseShellExecute = false };
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
            MessageBox.Show(owner, ex.Message, "Archive operation failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
