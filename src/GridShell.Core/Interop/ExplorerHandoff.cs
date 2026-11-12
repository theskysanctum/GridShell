using System.IO;

namespace GridShell.Core.Interop;

/// <summary>One-shot file-based handoff for the folder path accompanying
/// <see cref="OpenExplorerMessage"/> - a Win32 window message alone can't carry a string
/// across processes. Written by the (possibly second) process requesting the folder be
/// opened, read and deleted by GridShell.Grid's single running instance once it receives
/// the message.</summary>
public static class ExplorerHandoff
{
    private static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GridShell", "pending-explorer-path.txt");

    public static void Write(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, path);
    }

    public static string? ReadAndClear()
    {
        if (!File.Exists(FilePath))
        {
            return null;
        }

        try
        {
            var path = File.ReadAllText(FilePath);
            File.Delete(FilePath);
            return string.IsNullOrWhiteSpace(path) ? null : path;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
