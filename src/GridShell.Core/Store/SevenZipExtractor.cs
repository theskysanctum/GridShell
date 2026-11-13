using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

namespace GridShell.Core.Store;

/// <summary>Shells out to the bundled 7za.exe (7-Zip's official standalone console build, from
/// ip7z/7zip's own GitHub releases - verified live to run standalone with no companion DLL, and
/// to extract .7z correctly) since .NET has no built-in .7z support. Same Tools-folder/
/// AppContext.BaseDirectory convention as every other bundled CLI tool in this app (chdman,
/// extract-xiso, aria2c - see Aria2Daemon; also ArchiveOperations' own archiver). Shared by
/// GitHubReleaseInstaller and PackageInstaller - both extract a downloaded/bundled .7z the same
/// way.</summary>
public static class SevenZipExtractor
{
    /// <summary>Returns null on success, an error message otherwise.</summary>
    public static async Task<string?> ExtractAsync(string archivePath, string destinationDir)
    {
        var toolPath = Path.Combine(AppContext.BaseDirectory, "Tools", "7za.exe");
        if (!File.Exists(toolPath))
        {
            return "7za.exe (bundled 7-Zip extractor) is missing from this install.";
        }

        var startInfo = new ProcessStartInfo(toolPath, $"x \"{archivePath}\" -o\"{destinationDir}\" -y")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(startInfo);
        if (process is null)
        {
            return "Couldn't start 7za.exe.";
        }

        var output = await process.StandardOutput.ReadToEndAsync();
        var error = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();

        return process.ExitCode == 0 ? null : (string.IsNullOrWhiteSpace(error) ? output : error);
    }
}
