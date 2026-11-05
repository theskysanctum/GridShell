using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace GridShell;

/// <summary>Compress-to-CHD and Xbox ISO extraction, driven through the bundled chdman.exe /
/// extract-xiso.exe in the Tools folder next to this exe - both verified via their own
/// --help output (not guessed) before wiring up.</summary>
public static class RomToolOperations
{
    private static readonly string[] ChdSourceExtensions = { ".cue", ".gdi", ".iso" };

    public static bool IsChdSource(string path) =>
        ChdSourceExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);

    public static bool IsXisoCandidate(string path) =>
        string.Equals(Path.GetExtension(path), ".iso", StringComparison.OrdinalIgnoreCase);

    public static bool CompressToChd(string sourcePath, Window owner)
    {
        if (ToolPath("chdman.exe") is not { } chdman)
        {
            WarnMissingTool(owner, "chdman.exe");
            return false;
        }

        var outputPath = Path.ChangeExtension(sourcePath, ".chd");

        // .cue/.gdi are CD-track layouts (createcd); a bare .iso is treated as a DVD/single-
        // track image (createdvd) - chdman has no single command that auto-detects this.
        var command = string.Equals(Path.GetExtension(sourcePath), ".iso", StringComparison.OrdinalIgnoreCase)
            ? "createdvd"
            : "createcd";

        return Run(chdman, $"{command} -i \"{sourcePath}\" -o \"{outputPath}\" -f", owner);
    }

    public static bool ExtractXiso(string sourcePath, Window owner)
    {
        if (ToolPath("extract-xiso.exe") is not { } tool)
        {
            WarnMissingTool(owner, "extract-xiso.exe");
            return false;
        }

        var destinationDir = Path.GetDirectoryName(sourcePath) ?? ".";
        return Run(tool, $"-x -d \"{destinationDir}\" \"{sourcePath}\"", owner);
    }

    private static string? ToolPath(string exeName)
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Tools", exeName);
        return File.Exists(path) ? path : null;
    }

    private static void WarnMissingTool(Window owner, string exeName) =>
        MessageBox.Show(owner, $"{exeName} is missing from the Tools folder.", "ROM tool",
            MessageBoxButton.OK, MessageBoxImage.Warning);

    // Deliberately doesn't suppress the console window (no CreateNoWindow/redirect) - both
    // tools are plain console apps with no progress UI of their own, and CD/DVD compression
    // can take a while, so the console output is the only feedback the user gets that it's
    // actually working.
    private static bool Run(string exePath, string args, Window owner)
    {
        try
        {
            var psi = new ProcessStartInfo(exePath, args) { UseShellExecute = false };
            using var process = Process.Start(psi);
            process?.WaitForExit();
            return true;
        }
        catch (Exception ex) when (ex is IOException or Win32Exception)
        {
            MessageBox.Show(owner, ex.Message, "ROM tool failed", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }
    }
}
