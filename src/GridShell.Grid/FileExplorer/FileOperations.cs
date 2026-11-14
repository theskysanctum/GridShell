using System.IO;
using System.Windows;
using GridShell.Core.Tiles;
using Microsoft.VisualBasic.FileIO;

namespace GridShell;

/// <summary>File copy/move/delete/rename/create-folder, routed through
/// Microsoft.VisualBasic.FileIO.FileSystem rather than System.IO directly - that gives real
/// native Explorer progress dialogs, overwrite-conflict prompts, and Recycle Bin integration
/// for free, the same UI a real Explorer operation shows, instead of reimplementing all of
/// that by hand.</summary>
public static class FileOperations
{
    public static bool TryCopy(string sourcePath, string destinationDir, bool isDirectory, Window owner)
    {
        var destination = Path.Combine(destinationDir, Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar)));
        return TryRun(owner, () =>
        {
            if (isDirectory)
            {
                FileSystem.CopyDirectory(sourcePath, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
            }
            else
            {
                FileSystem.CopyFile(sourcePath, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
            }
        });
    }

    public static bool TryMove(string sourcePath, string destinationDir, bool isDirectory, Window owner)
    {
        var destination = Path.Combine(destinationDir, Path.GetFileName(sourcePath.TrimEnd(Path.DirectorySeparatorChar)));
        return TryRun(owner, () =>
        {
            if (isDirectory)
            {
                FileSystem.MoveDirectory(sourcePath, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
            }
            else
            {
                FileSystem.MoveFile(sourcePath, destination, UIOption.AllDialogs, UICancelOption.DoNothing);
            }
        });
    }

    /// <summary>Sends to the Recycle Bin (not a permanent delete) with Explorer's own
    /// confirmation prompt - matches what pressing Delete in real Explorer does.</summary>
    public static bool TryDelete(string path, bool isDirectory, Window owner) =>
        TryRun(owner, () =>
        {
            if (isDirectory)
            {
                FileSystem.DeleteDirectory(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
            }
            else
            {
                FileSystem.DeleteFile(path, UIOption.AllDialogs, RecycleOption.SendToRecycleBin, UICancelOption.DoNothing);
            }
        });

    public static bool TryDeleteGameFile(Tile tile, Window owner)
    {
        if (tile.Kind != TileKind.Game || tile.Platform is null)
        {
            return true;
        }

        var path = GetRomPath(tile.Arguments);
        if (string.IsNullOrWhiteSpace(path))
        {
            new ToastWindow("Could not determine the ROM file for this game.", TimeSpan.FromSeconds(5), isWarning: true).Show();
            return false;
        }

        return !File.Exists(path) || TryDelete(path, isDirectory: false, owner);
    }

    private static string? GetRomPath(string? arguments)
    {
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return null;
        }

        var quotedPaths = arguments.Split('"')
            .Where((_, index) => index % 2 == 1)
            .Where(Path.IsPathFullyQualified)
            .ToList();

        return quotedPaths.LastOrDefault()
            ?? arguments.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .LastOrDefault(Path.IsPathFullyQualified);
    }

    public static bool TryRename(string path, string newName, bool isDirectory, Window owner)
    {
        var destination = Path.Combine(Path.GetDirectoryName(path.TrimEnd(Path.DirectorySeparatorChar)) ?? "", newName);
        return TryRun(owner, () =>
        {
            if (isDirectory)
            {
                Directory.Move(path, destination);
            }
            else
            {
                File.Move(path, destination);
            }
        });
    }

    public static bool TryCreateFolder(string parentDir, string name, Window owner) =>
        TryRun(owner, () => Directory.CreateDirectory(Path.Combine(parentDir, name)));

    private static bool TryRun(Window owner, Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (OperationCanceledException)
        {
            // The user cancelled a dialog FileSystem itself showed (overwrite prompt, progress
            // dialog's own Cancel) - not an error worth reporting again on top of that.
            return false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            new ToastWindow($"File operation failed: {ex.Message}", TimeSpan.FromSeconds(5), isWarning: true).Show();
            return false;
        }
    }
}
