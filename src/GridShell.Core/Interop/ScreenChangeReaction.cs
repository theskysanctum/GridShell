using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Re-centers a dialog-style window within the current screen (or its owner) whenever
/// the display configuration changes — resolution, DPI/scaling, refresh rate, or orientation —
/// while it's open. WPF's WindowStartupLocation only positions a window once, at its first
/// Show(), and never revisits that position on its own, so a window left open through a live
/// display change can end up partly or fully off the new screen bounds.</summary>
public static class ScreenChangeReaction
{
    /// <summary>Convenience hook for a dialog-style window's constructor — no other wiring
    /// needed at the call site.</summary>
    public static void RecenterOnDisplayChange(this Window window)
    {
        // GetSystemMetrics/the work area can still be mid-transition for a brief moment right
        // when WM_DISPLAYCHANGE/WM_DPICHANGED first arrives — re-centering again shortly after
        // (once the OS has actually settled) catches anything the immediate call read a stale/
        // transitional value for (confirmed empirically on the taskbar/grid's identical timer).
        var settleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;

            settleTimer.Tick += (_, _) =>
            {
                settleTimer.Stop();
                Recenter(window, hwnd);
            };

            HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
            {
                if (msg == (int)WindowMessage.WM_DISPLAYCHANGE || msg == (int)WindowMessage.WM_DPICHANGED)
                {
                    Recenter(window, hwnd);
                    settleTimer.Stop();
                    settleTimer.Start();
                }

                return IntPtr.Zero;
            });
        };

        window.Closed += (_, _) => settleTimer.Stop();
    }

    private static void Recenter(Window window, IntPtr hwnd)
    {
        var workArea = AppBarManager.GetWorkArea();
        var width = (int)(window.ActualWidth * GetDpiScale(hwnd));
        var height = (int)(window.ActualHeight * GetDpiScale(hwnd));

        var areaWidth = workArea.right - workArea.left;
        var areaHeight = workArea.bottom - workArea.top;

        var left = workArea.left + Math.Max(0, (areaWidth - width) / 2);
        var top = workArea.top + Math.Max(0, (areaHeight - height) / 2);

        SetWindowPos(new HWND(hwnd), HWND.NULL, left, top, 0, 0,
            SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);
    }

    /// <summary>Sizes a dialog-style window as a fraction of the current work area (clamped to a
    /// min/max range, in DIPs) and keeps it centered — both at first show and again on any live
    /// display change (resolution, DPI/scaling, orientation). Unlike <see
    /// cref="RecenterOnDisplayChange"/>, this is for windows whose fixed pixel size could clip
    /// their own content on a smaller screen/resolution; the window's XAML should use
    /// WindowStartupLocation="Manual" and no fixed Width/Height, since both are fully driven by
    /// this instead.</summary>
    public static void ScaleToWorkArea(
        this Window window, double widthFraction, double heightFraction,
        double minWidth, double minHeight, double maxWidth, double maxHeight)
    {
        var settleTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };

        window.SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            ApplyScale(hwnd, widthFraction, heightFraction, minWidth, minHeight, maxWidth, maxHeight);

            settleTimer.Tick += (_, _) =>
            {
                settleTimer.Stop();
                ApplyScale(hwnd, widthFraction, heightFraction, minWidth, minHeight, maxWidth, maxHeight);
            };

            HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr _, int msg, IntPtr _, IntPtr _, ref bool handled) =>
            {
                if (msg == (int)WindowMessage.WM_DISPLAYCHANGE || msg == (int)WindowMessage.WM_DPICHANGED)
                {
                    ApplyScale(hwnd, widthFraction, heightFraction, minWidth, minHeight, maxWidth, maxHeight);
                    settleTimer.Stop();
                    settleTimer.Start();
                }

                return IntPtr.Zero;
            });
        };

        window.Closed += (_, _) => settleTimer.Stop();
    }

    private static void ApplyScale(
        IntPtr hwnd, double widthFraction, double heightFraction,
        double minWidth, double minHeight, double maxWidth, double maxHeight)
    {
        var workArea = AppBarManager.GetWorkArea();
        var scale = GetDpiScale(hwnd);

        var areaWidth = workArea.right - workArea.left;
        var areaHeight = workArea.bottom - workArea.top;

        var width = (int)(Math.Clamp(areaWidth * widthFraction / scale, minWidth, maxWidth) * scale);
        var height = (int)(Math.Clamp(areaHeight * heightFraction / scale, minHeight, maxHeight) * scale);

        // Never exceed the actual work area, however generous min/max are (a tiny screen still
        // needs a usable size, but it can't be bigger than the screen itself).
        width = Math.Min(width, areaWidth);
        height = Math.Min(height, areaHeight);

        var left = workArea.left + Math.Max(0, (areaWidth - width) / 2);
        var top = workArea.top + Math.Max(0, (areaHeight - height) / 2);

        SetWindowPos(new HWND(hwnd), HWND.NULL, left, top, width, height,
            SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);
    }

    private static double GetDpiScale(IntPtr hwnd) => GetDpiForWindow(new HWND(hwnd)) / 96.0;
}
