using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Forces a window to be excluded from GridShell's own taskbar window list, deterministically
/// and immediately — unlike WPF's own ShowInTaskbar="False", which only sometimes translates into
/// WS_EX_TOOLWINDOW/owner-exclusion in time for the taskbar's event-driven tracker to see it that
/// way at window-creation time (verified: a settled window is correctly excluded by
/// WindowEnumerator's existing logic, but the tracker can catch it mid-settle and never re-check
/// once no further window event fires). Directly setting WS_EX_TOOLWINDOW ourselves at
/// SourceInitialized has no such race.</summary>
public static class TaskbarExclusion
{
    public static void Apply(HWND hwnd)
    {
        var exStyle = (WindowStylesEx)GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);
        SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE, (int)(exStyle | WindowStylesEx.WS_EX_TOOLWINDOW));
    }

    /// <summary>Convenience hook for a dialog-style window's constructor — applies itself once
    /// the window's handle exists, with no other wiring needed at the call site.</summary>
    public static void ExcludeFromTaskbar(this Window window)
    {
        window.SourceInitialized += (_, _) =>
            Apply(new WindowInteropHelper(window).Handle);
    }
}
