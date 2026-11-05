using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Windows.Threading;
using GridShell.Core.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Taskbar;

/// <summary>Keeps an ObservableCollection of eligible top-level windows in sync, re-enumerating
/// (debounced) on any relevant window event rather than tracking each change incrementally —
/// simpler and plenty cheap given typical open-window counts.</summary>
public sealed class TaskbarWindowTracker : IDisposable
{
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
        foreach (var hwnd in current)
        {
            if (!existingHandles.Contains(hwnd))
            {
                var ownIcon = ShellIcons.GetWindowOwnIcon(hwnd);
                Windows.Add(new TaskbarWindowEntry
                {
                    Handle = hwnd,
                    Title = GetTitle(hwnd),
                    Icon = ownIcon ?? ShellIcons.GetProcessIconForWindow(hwnd),
                    HasOwnIcon = ownIcon is not null,
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
            if (!entry.HasOwnIcon && ShellIcons.GetWindowOwnIcon(entry.Handle) is { } ownIcon)
            {
                entry.Icon = ownIcon;
                entry.HasOwnIcon = true;
            }
        }
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
