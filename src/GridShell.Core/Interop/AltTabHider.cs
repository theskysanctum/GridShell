using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Excludes a window from Alt+Tab (and the taskbar) by setting WS_EX_TOOLWINDOW and
/// explicitly clearing WS_EX_APPWINDOW - WPF's own ShowInTaskbar="False" sets WS_EX_TOOLWINDOW
/// too, but doesn't reliably take effect on the native HWND by itself (same class of issue
/// WindowIconHider works around for the title-bar icon), so this applies it directly via
/// SetWindowLong + SWP_FRAMECHANGED, the same proven pattern.</summary>
public static class AltTabHider
{
    public static void Apply(HWND hwnd)
    {
        var exStyle = (WindowStylesEx)GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);
        exStyle |= WindowStylesEx.WS_EX_TOOLWINDOW;
        exStyle &= ~WindowStylesEx.WS_EX_APPWINDOW;
        SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE, (int)exStyle);
        SetWindowPos(hwnd, HWND.NULL, 0, 0, 0, 0,
            SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOZORDER
            | SetWindowPosFlags.SWP_NOACTIVATE | SetWindowPosFlags.SWP_FRAMECHANGED);
    }

    public static void HideFromAltTab(this Window window) =>
        window.SourceInitialized += (_, _) =>
            Apply(new HWND(new WindowInteropHelper(window).Handle));
}
