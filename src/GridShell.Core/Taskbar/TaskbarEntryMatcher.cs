using System.Diagnostics;
using System.Linq;
using GridShell.Core.Tiles;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Taskbar;

/// <summary>Decides whether a top-level window belongs in GridShell's own taskbar at all, and
/// if so what icon/glyph it should show there. "In GridShell" means either one of GridShell's
/// own first-party processes (the taskbar itself, the grid/Store/Settings/etc., the watchdog),
/// or a process whose exe matches the TargetPath/BrowserExecutablePath of a Tile the user
/// actually added - a window from some unrelated app the user opened outside GridShell has no
/// match and gets excluded entirely, same "GridShell is the whole environment" reasoning
/// already behind hiding install-process windows and forcing taskbar ordering elsewhere in
/// this codebase.
///
/// Same Material Symbols codepoints already used elsewhere for visual consistency - &#xe8fd;
/// is the taskbar's own controller-hint button, &#xe8d1; is the grid's own Store button.</summary>
public static class TaskbarEntryMatcher
{
    private const string StoreWindowTitle = "GridShell Store";
    public const string GameControllerGlyph = "";
    public const string StoreGlyph = "";

    private static readonly HashSet<string> GridShellProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "GridShell", "GridShell.Grid", "GridShell.Watchdog",
    };

    public readonly record struct Match(string? Glyph, string? CustomIconPath);

    public readonly record struct ResolvedTile(string? ExePath, TileKind Kind, string? CustomIconPath);

    /// <summary>Resolves every tile's real exe path once per refresh cycle - an "appx:"
    /// TargetPath needs a live WinRT PackageManager lookup (TileLauncher.ResolveTargetPath),
    /// which is too expensive to repeat per-window on every refresh when Resolve below is
    /// called once per open window.</summary>
    public static List<ResolvedTile> ResolveTiles(List<Tile> tiles) => tiles.Select(tile => new ResolvedTile(
        tile.Kind == TileKind.Website ? tile.BrowserExecutablePath : TileLauncher.ResolveTargetPath(tile.TargetPath) ?? tile.TargetPath,
        tile.Kind,
        tile.CustomIconPath)).ToList();

    /// <summary>Null means "not in GridShell" - the caller should exclude this window from the
    /// taskbar entirely rather than falling back to any icon for it.</summary>
    public static Match? Resolve(HWND hwnd, string title, List<ResolvedTile> resolvedTiles)
    {
        string processName;
        string? exePath;
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            using var process = Process.GetProcessById((int)pid);
            processName = process.ProcessName;
            exePath = process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Exited mid-lookup, or an elevated process we can't inspect - exclude rather than
            // guess at whether it belongs.
            return null;
        }

        if (GridShellProcessNames.Contains(processName))
        {
            return string.Equals(title, StoreWindowTitle, StringComparison.Ordinal)
                ? new Match(StoreGlyph, null)
                : new Match(null, null);
        }

        if (exePath is null)
        {
            return null;
        }

        foreach (var tile in resolvedTiles)
        {
            if (tile.ExePath is null || !string.Equals(tile.ExePath, exePath, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return tile.Kind == TileKind.Game
                ? new Match(GameControllerGlyph, null)
                : new Match(null, tile.CustomIconPath);
        }

        return null;
    }
}
