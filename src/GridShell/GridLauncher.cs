using System.Diagnostics;
using System.Linq;
using System.Threading;
using GridShell.Core.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell;

/// <summary>Owns the GridShell.Grid process. It's pre-warmed hidden right when the taskbar
/// starts, so a toggle is normally just a cross-process show/hide message to its already-running
/// window — not a fresh process launch — which is what removes the open-grid delay. Only falls
/// back to a cold-started launch if the pre-warmed process isn't there yet or has crashed.</summary>
public sealed class GridLauncher
{
    public void WarmUp()
    {
        Process.Start(GetGridExePath());
    }

    /// <summary>Closes the running grid process — needed because it's now long-lived (unlike
    /// the old per-toggle process), so it would otherwise outlive the taskbar and keep running
    /// invisibly in the background whenever GridShell exits.</summary>
    public void Shutdown()
    {
        foreach (var process in Process.GetProcessesByName("GridShell.Grid"))
        {
            ProcessWindowCloser.CloseAnyWindowOf(process.Id);
        }
    }

    public void Toggle()
    {
        var hwnds = FindGridWindowWithRetry();
        if (hwnds.Count > 0)
        {
            foreach (var hwnd in hwnds)
            {
                PostMessage(hwnd, GridToggleMessage.Id, IntPtr.Zero, IntPtr.Zero);
            }

            return;
        }

        // No running instance found at all (never pre-warmed, or it crashed) — cold-start it,
        // with --show since the user is asking for it right now. GridShell.Grid's own
        // single-instance guard means this is always safe to call even if one genuinely is
        // already running somewhere we just failed to find in time: a second launch forwards
        // its "--show" intent to the real instance and exits, rather than duplicating it.
        Process.Start(GetGridExePath(), "--show");
    }

    /// <summary>Finds the running grid's window(s) by process name rather than a remembered PID —
    /// a toggle that raced the pre-warmed process's own startup (window not created yet) and
    /// fell through to a cold-started second launch would otherwise leave a stale PID reference
    /// to a process that immediately exits under GridShell.Grid's single-instance guard, breaking
    /// every toggle after it. Re-resolving "whichever process currently owns this name" each time
    /// sidesteps that entirely — there's always at most one real instance, guaranteed by that
    /// same guard. The retry loop guards the narrow startup race where Win is tapped before the
    /// pre-warmed process's window exists yet.</summary>
    private static List<HWND> FindGridWindowWithRetry()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var hwnds = Process.GetProcessesByName("GridShell.Grid")
                .SelectMany(p => ProcessWindowCloser.FindWindowsOf(p.Id))
                .ToList();
            if (hwnds.Count > 0)
            {
                return hwnds;
            }

            Thread.Sleep(50);
        }

        return new List<HWND>();
    }

    private static string GetGridExePath() => SiblingExePath.Resolve("GridShell.Grid", "GridShell.Grid.exe");
}
