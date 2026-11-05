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
    private DateTime _lastLoad = DateTime.MinValue;

    public bool IsGameForeground()
    {
        RefreshIfStale();

        var hwnd = GetForegroundWindow();
        if (hwnd.IsNull)
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out var pid);

        try
        {
            using var process = Process.GetProcessById((int)pid);
            var path = process.MainModule?.FileName;
            return path is not null && _gamePaths.Contains(path);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
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
            var gameTilePaths = TileStore.Load()
                .Where(t => t.Kind == TileKind.Game)
                .Select(t => t.TargetPath);

            var configuredEmulatorPaths = AppSettingsStore.Load().EmulatorConfigs.Values
                .Select(c => c.EmulatorPath)
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => path!);

            _gamePaths = gameTilePaths.Concat(configuredEmulatorPaths)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Keep the previous (possibly stale) set rather than suppressing nothing.
        }
    }
}
