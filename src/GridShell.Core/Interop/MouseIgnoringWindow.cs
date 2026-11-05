using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Makes a window invisible to mouse hit-testing (WS_EX_TRANSPARENT) — clicks/hover
/// pass straight through to whatever's behind it instead of reaching its own controls. Used for
/// the on-screen keyboard, which is meant to be driven by D-pad/controller only; without this,
/// the real cursor (or the controller's own synthetic mouse, via SyntheticMouse/SendInput) could
/// still click its keys.</summary>
public static class MouseIgnoringWindow
{
    public static void Apply(HWND hwnd)
    {
        var exStyle = (WindowStylesEx)GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);
        SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE, (int)(exStyle | WindowStylesEx.WS_EX_TRANSPARENT));
    }

    public static void IgnoreMouseInput(this Window window)
    {
        window.SourceInitialized += (_, _) =>
            Apply(new WindowInteropHelper(window).Handle);
    }
}
