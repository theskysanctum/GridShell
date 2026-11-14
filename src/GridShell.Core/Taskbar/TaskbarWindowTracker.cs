using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using GridShell.Core.Interop;
using GridShell.Core.Tiles;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Taskbar;

/// <summary>Keeps an ObservableCollection of eligible top-level windows in sync, re-enumerating
/// (debounced) on any relevant window event rather than tracking each change incrementally —
/// simpler and plenty cheap given typical open-window counts.</summary>
public sealed class TaskbarWindowTracker : IDisposable
{
    private const string StoreWindowTitle = "GridShell Store";

    // Same Material Symbols codepoint the grid's own Store button already uses - visual
    // consistency, not a new glyph.
    private const string StoreGlyph = "";

    private readonly WindowEventWatcher _watcher = new();
    private readonly DispatcherTimer _debounce;

    // Belt-and-suspenders: a window can become ineligible (e.g. Windows Settings navigating
    // back to its home page, whose title exactly matches the excluded string) without firing
    // an event our watcher catches, leaving a stale entry in the list indefinitely. A slow
    // periodic refresh — independent of any specific event firing — guarantees it's cleaned up
    // within a couple of seconds either way.
    private readonly DispatcherTimer _periodicRefresh;

    // A freshly created "--app=" window often hasn't set its real favicon-based icon yet at
    // the instant it first appears (Chromium sets it asynchronously once the page's favicon
    // loads). Retrying only on the window-change debounce above made that upgrade's timing
    // depend on whatever OTHER window events happened to fire in the meantime — sometimes
    // fast, sometimes not for several seconds. This runs on its own short, fixed interval
    // instead, so the real icon shows up quickly and consistently either way.
    private readonly DispatcherTimer _iconRetry;

    public ObservableCollection<TaskbarWindowEntry> Windows { get; } = new();

    public TaskbarWindowTracker()
    {
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
        _debounce.Tick += (_, _) =>
        {
            _debounce.Stop();
            Refresh();
        };

        _watcher.Changed += () =>
        {
            _debounce.Stop();
            _debounce.Start();
        };

        _iconRetry = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _iconRetry.Tick += (_, _) => RetryPendingIcons();

        _periodicRefresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _periodicRefresh.Tick += (_, _) => Refresh();
    }

    public void Start()
    {
        Refresh();
        _watcher.Start();
        _iconRetry.Start();
        _periodicRefresh.Start();
    }

    public void Stop()
    {
        _watcher.Stop();
        _iconRetry.Stop();
        _periodicRefresh.Stop();
    }

    private void Refresh()
    {
        var current = WindowEnumerator.EnumerateEligibleWindows();
        var currentSet = new HashSet<HWND>(current);

        for (var i = Windows.Count - 1; i >= 0; i--)
        {
            if (!currentSet.Contains(Windows[i].Handle))
            {
                Windows.RemoveAt(i);
            }
        }

        var existingHandles = new HashSet<HWND>(Windows.Select(w => w.Handle));
        var newHandles = current.Where(hwnd => !existingHandles.Contains(hwnd)).ToList();
        if (newHandles.Count > 0)
        {
            // Resolved once per refresh, not once per window - an "appx:" TargetPath needs a
            // live WinRT PackageManager lookup, too expensive to repeat per-window.
            var tileExePaths = ResolveTileExePaths();
            foreach (var hwnd in newHandles)
            {
                var title = GetTitle(hwnd);
                if (string.Equals(title, StoreWindowTitle, StringComparison.Ordinal))
                {
                    // Same glyph the grid's own Store button already uses - never GridShell's
                    // generic window icon for this one.
                    Windows.Add(new TaskbarWindowEntry { Handle = hwnd, Title = title, Glyph = StoreGlyph });
                    continue;
                }

                var customIconPath = FindMatchingCustomIconPath(hwnd, tileExePaths);
                var ownIcon = customIconPath is null ? ShellIcons.GetWindowOwnIcon(hwnd) : null;
                Windows.Add(new TaskbarWindowEntry
                {
                    Handle = hwnd,
                    Title = title,
                    // The same icon the grid itself shows for the matching tile, when one
                    // exists and has a custom icon - not a separately shell-extracted one.
                    Icon = customIconPath is not null ? ImageLoader.LoadFromFile(customIconPath) : ownIcon ?? ShellIcons.GetProcessIconForWindow(hwnd),
                    HasOwnIcon = customIconPath is not null || ownIcon is not null,
                });
            }
        }

        var foreground = GetForegroundWindow();
        foreach (var entry in Windows)
        {
            entry.Title = GetTitle(entry.Handle);
            entry.IsActive = entry.Handle == foreground;
        }
    }

    private void RetryPendingIcons()
    {
        foreach (var entry in Windows)
        {
            if (!entry.HasGlyph && !entry.HasOwnIcon && ShellIcons.GetWindowOwnIcon(entry.Handle) is { } ownIcon)
            {
                entry.Icon = ownIcon;
                entry.HasOwnIcon = true;
            }
        }
    }

    /// <summary>Every current Tile's own real exe path (App/Game: TargetPath, resolving an
    /// "appx:" prefix the same way TileLauncher does; Website: BrowserExecutablePath) paired
    /// with its CustomIconPath - null entries (no custom icon set) are skipped, since they'd
    /// never win over the shell-icon fallback anyway.</summary>
    private static List<(string ExePath, string CustomIconPath)> ResolveTileExePaths()
    {
        var result = new List<(string ExePath, string CustomIconPath)>();
        foreach (var tile in TileStore.Load())
        {
            if (tile.CustomIconPath is not { } customIconPath)
            {
                continue;
            }

            var exePath = tile.Kind == TileKind.Website
                ? tile.BrowserExecutablePath
                : TileLauncher.ResolveTargetPath(tile.TargetPath) ?? tile.TargetPath;

            if (exePath is not null)
            {
                result.Add((exePath, customIconPath));
            }
        }

        return result;
    }

    private static string? FindMatchingCustomIconPath(HWND hwnd, List<(string ExePath, string CustomIconPath)> tileExePaths)
    {
        if (tileExePaths.Count == 0)
        {
            return null;
        }

        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            using var process = Process.GetProcessById((int)pid);
            var exePath = process.MainModule?.FileName;
            if (exePath is null)
            {
                return null;
            }

            foreach (var (tileExePath, customIconPath) in tileExePaths)
            {
                if (string.Equals(tileExePath, exePath, StringComparison.OrdinalIgnoreCase))
                {
                    return customIconPath;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // Exited mid-lookup, or an elevated process we can't inspect - fall back to the
            // normal shell-icon resolution for this one instead of failing it outright.
        }

        return null;
    }

    private static string GetTitle(HWND hwnd)
    {
        var length = GetWindowTextLength(hwnd);
        if (length == 0)
        {
            return "";
        }

        var sb = new StringBuilder(length + 1);
        GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    public static void Activate(TaskbarWindowEntry entry) => WindowEnumerator.Activate(entry.Handle);

    public static void Minimize(TaskbarWindowEntry entry) =>
        ShowWindow(entry.Handle, ShowWindowCommand.SW_MINIMIZE);

    public void Dispose() => _watcher.Dispose();
}
