using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Wraps the AppBar API (SHAppBarMessage) so a window can reserve real screen-edge
/// space the way the real Windows taskbar does — this is what actually shrinks
/// SystemParameters.WorkArea for every app on the system, not just GridShell's own windows.
///
/// SHAppBarMessage alone turned out to be insufficient: it reliably shrinks the work area when
/// Explorer is running (even just as an incidental secondary instance), but when GridShell is
/// registered as the actual Windows shell and Explorer is genuinely not running at all, nothing
/// is left to process the AppBar message and the work area never updates — confirmed empirically
/// (sidebar reservation worked with Explorer present, silently did nothing without it). So this
/// also calls SystemParametersInfo(SPI_SETWORKAREA) directly, which needs no Explorer involvement
/// at all, as a belt-and-suspenders fix that covers both cases.</summary>
public static class AppBarManager
{
    public static readonly uint CallbackMessageId = RegisterWindowMessage("GridShellAppBarMessage");

    /// <summary>Registers hwnd as an AppBar docked to the given edge with the given thickness,
    /// and returns the final screen rect it was granted (position the window to this).</summary>
    public static RECT Register(HWND hwnd, ABE edge, double thickness)
    {
        var abd = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            uCallbackMessage = CallbackMessageId,
        };

        SHAppBarMessage(ABM.ABM_NEW, ref abd);

        // Real physical pixels (GetSystemMetrics), not SystemParameters.PrimaryScreenHeight —
        // the AppBar API works in screen pixels and WPF's version is DPI-scaled, which would
        // undersize the reservation on any display above 100% scaling.
        var screenHeight = GetSystemMetrics(SystemMetric.SM_CYSCREEN);
        var screenWidth = GetSystemMetrics(SystemMetric.SM_CXSCREEN);

        abd.uEdge = edge;
        abd.rc = edge switch
        {
            ABE.ABE_LEFT => new RECT(0, 0, (int)thickness, screenHeight),
            ABE.ABE_RIGHT => new RECT(screenWidth - (int)thickness, 0, screenWidth, screenHeight),
            ABE.ABE_TOP => new RECT(0, 0, screenWidth, (int)thickness),
            _ => new RECT(0, screenHeight - (int)thickness, screenWidth, screenHeight),
        };

        SHAppBarMessage(ABM.ABM_QUERYPOS, ref abd);

        // The system clamps the perpendicular extent to avoid overlapping other registered
        // app bars — notably the real Explorer taskbar, whose AppBar reservation survives
        // even though we've hidden its window (hiding a window doesn't unregister its app
        // bar). We've intentionally taken over this edge, so force the full screen extent
        // instead of respecting that now-irrelevant reservation; only keep the system's
        // thickness-dimension adjustment (relevant if another bar shares our own edge).
        var finalRect = edge switch
        {
            ABE.ABE_LEFT => new RECT(abd.rc.left, 0, abd.rc.left + (int)thickness, screenHeight),
            ABE.ABE_RIGHT => new RECT(abd.rc.right - (int)thickness, 0, abd.rc.right, screenHeight),
            ABE.ABE_TOP => new RECT(0, abd.rc.top, screenWidth, abd.rc.top + (int)thickness),
            _ => new RECT(0, abd.rc.bottom - (int)thickness, screenWidth, abd.rc.bottom),
        };
        abd.rc = finalRect;

        // ABM_SETPOS re-clamps abd.rc in place the same way ABM_QUERYPOS did — for the exact
        // same reason (it still sees Explorer's "live" bottom reservation) — so the struct
        // coming back out of this call is NOT what we want to position the window to. Use
        // our own forced rect instead of whatever SETPOS hands back.
        SHAppBarMessage(ABM.ABM_SETPOS, ref abd);

        var state = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
            lParam = (IntPtr)ABS.ABS_ALWAYSONTOP,
        };
        SHAppBarMessage(ABM.ABM_SETSTATE, ref state);

        ReserveWorkArea(edge, (int)thickness, screenWidth, screenHeight);

        return finalRect;
    }

    public static void Unregister(HWND hwnd, ABE edge)
    {
        var abd = new APPBARDATA
        {
            cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<APPBARDATA>(),
            hWnd = hwnd,
        };

        SHAppBarMessage(ABM.ABM_REMOVE, ref abd);

        var screenHeight = GetSystemMetrics(SystemMetric.SM_CYSCREEN);
        var screenWidth = GetSystemMetrics(SystemMetric.SM_CXSCREEN);
        ReleaseWorkArea(edge, screenWidth, screenHeight);
    }

    /// <summary>Shrinks just our own edge of the current work area, preserving whatever other
    /// edges are already reserved (e.g. Explorer's own taskbar, when it happens to be present) —
    /// rather than overwriting the whole rect, which would clobber those other reservations.</summary>
    private static void ReserveWorkArea(ABE edge, int thickness, int screenWidth, int screenHeight)
    {
        var current = GetWorkArea();
        var updated = edge switch
        {
            ABE.ABE_LEFT => new RECT(thickness, current.top, current.right, current.bottom),
            ABE.ABE_RIGHT => new RECT(current.left, current.top, screenWidth - thickness, current.bottom),
            ABE.ABE_TOP => new RECT(current.left, thickness, current.right, current.bottom),
            _ => new RECT(current.left, current.top, current.right, screenHeight - thickness),
        };
        SetWorkArea(updated);
    }

    private static void ReleaseWorkArea(ABE edge, int screenWidth, int screenHeight)
    {
        var current = GetWorkArea();
        var updated = edge switch
        {
            ABE.ABE_LEFT => new RECT(0, current.top, current.right, current.bottom),
            ABE.ABE_RIGHT => new RECT(current.left, current.top, screenWidth, current.bottom),
            ABE.ABE_TOP => new RECT(current.left, 0, current.right, current.bottom),
            _ => new RECT(current.left, current.top, current.right, screenHeight),
        };
        SetWorkArea(updated);
    }

    /// <summary>The current work area in real physical pixels, read directly from the OS —
    /// unlike SystemParameters.WorkArea, this can't be stale relative to a WM_DISPLAYCHANGE/
    /// WM_DPICHANGED message a caller is reacting to, since WPF's own copy only refreshes via
    /// its own internal listener, whose ordering relative to a caller's own hook isn't
    /// guaranteed.</summary>
    public static RECT GetWorkArea()
    {
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
        try
        {
            SystemParametersInfo(SPI.SPI_GETWORKAREA, 0, ptr, 0);
            return Marshal.PtrToStructure<RECT>(ptr);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void SetWorkArea(RECT rect)
    {
        var ptr = Marshal.AllocHGlobal(Marshal.SizeOf<RECT>());
        try
        {
            Marshal.StructureToPtr(rect, ptr, false);
            SystemParametersInfo(SPI.SPI_SETWORKAREA, 0, ptr, SPIF.SPIF_UPDATEINIFILE | SPIF.SPIF_SENDCHANGE);
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }
}
