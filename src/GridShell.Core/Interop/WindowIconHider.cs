using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Hides a window's title-bar icon (and the system-menu click target it doubles as)
/// while keeping the title bar itself and its Minimize/Maximize/Close buttons - the standard
/// documented technique (Microsoft's own WPF SDK team blog sample): add WS_EX_DLGMODALFRAME to
/// the window's extended style, then SWP_FRAMECHANGED to force the non-client frame to redraw
/// without it. WM_SETICON with null handles on top, since WPF falls back to the application's
/// own icon for any window that doesn't set one explicitly, and the style flag alone doesn't
/// always clear that.</summary>
public static class WindowIconHider
{
    public static void Apply(HWND hwnd)
    {
        var exStyle = (WindowStylesEx)GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);
        SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE, (int)(exStyle | WindowStylesEx.WS_EX_DLGMODALFRAME));
        SetWindowPos(hwnd, HWND.NULL, 0, 0, 0, 0,
            SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER
            | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_FRAMECHANGED);

        const int ICON_SMALL = 0;
        const int ICON_BIG = 1;
        SendMessage(hwnd, (uint)WindowMessage.WM_SETICON, (IntPtr)ICON_SMALL, IntPtr.Zero);
        SendMessage(hwnd, (uint)WindowMessage.WM_SETICON, (IntPtr)ICON_BIG, IntPtr.Zero);
    }

    public static void HideWindowIcon(this Window window) =>
        window.SourceInitialized += (_, _) =>
            Apply(new HWND(new WindowInteropHelper(window).Handle));
}
