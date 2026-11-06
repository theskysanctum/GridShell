using System.Diagnostics;
using System.Linq;
using GridShell.Core.Settings;
using GridShell.Core.Tiles;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Detects whether the currently foreground window belongs to one of the user's own
/// Game-kind tiles, or to a configured emulator running on its own (e.g. opened from Settings
/// to map controls, with no ROM/tile involved yet) — used to suppress controller-to-mouse
/// emulation and the OSK trigger while an actual game (or an emulator's own input-binding
/// screen, which needs raw, unintercepted controller input the same way) has focus. Uses
/// GridShell's own curated game/emulator lists rather than guessing from window style/fullscreen
/// state, since that's unreliable (plenty of real games run windowed, plenty of non-games run
/// fullscreen).</summary>
public sealed class GameForegroundDetector
{
    private static readonly TimeSpan CacheDuration = TimeSpan.FromSeconds(3);

    private HashSet<string> _gamePaths = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Subset of _gamePaths whose tile opted in via Tile.PauseWallpaperWhileRunning -
    /// deliberately separate from _gamePaths itself, since that set's own (unrelated) consumer,
    /// controller/OSK suppression, must stay blanket for every game regardless of this flag.</summary>
    private HashSet<string> _pauseWallpaperPaths = new(StringComparer.OrdinalIgnoreCase);
    private DateTime _lastLoad = DateTime.MinValue;

    public bool IsGameForeground() => _gamePaths.Contains(GetForegroundProcessPath() ?? "");

    /// <summary>Whether the desktop wallpaper's video should be paused right now - true only
    /// when the foreground process belongs to a Game tile that opted in.</summary>
    public bool ShouldPauseWallpaper() => _pauseWallpaperPaths.Contains(GetForegroundProcessPath() ?? "");

    private string? GetForegroundProcessPath()
    {
        RefreshIfStale();

        var hwnd = GetForegroundWindow();
        if (hwnd.IsNull)
        {
            return null;
        }

        GetWindowThreadProcessId(hwnd, out var pid);

        try
        {
            using var process = Process.GetProcessById((int)pid);
            return process.MainModule?.FileName;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return null;
        }
    }

    private void RefreshIfStale()
    {
        if (DateTime.UtcNow - _lastLoad < CacheDuration)
        {
            return;
        }

        _lastLoad = DateTime.UtcNow;

        try
        {
            var gameTiles = TileStore.Load().Where(t => t.Kind == TileKind.Game).ToList();

            var configuredEmulatorPaths = AppSettingsStore.Load().EmulatorConfigs.Values
                .Select(c => c.EmulatorPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!);

            _gamePaths = gameTiles.Select(t => t.TargetPath).Concat(configuredEmulatorPaths)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            _pauseWallpaperPaths = gameTiles.Where(t => t.PauseWallpaperWhileRunning)
                .Select(t => t.TargetPath)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Keep the previous (possibly stale) sets rather than suppressing nothing.
        }
    }
}
