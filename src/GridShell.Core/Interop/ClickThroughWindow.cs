using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Makes a window transparent to mouse hit-testing - clicks fall straight through to
/// whatever's underneath, as if the window weren't there. Used for desktop widgets (video/image
/// only, nothing on them a click could ever need to reach).</summary>
public static class ClickThroughWindow
{
    public static void Apply(HWND hwnd)
    {
        var exStyle = (WindowStylesEx)GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);

        // WS_EX_TRANSPARENT is the actual click-through switch, and needs nothing else to
        // work. WS_EX_LAYERED deliberately NOT added here - a previous version of this code
        // added it unverified ("required alongside WS_EX_TRANSPARENT"), which was never
        // actually true and caused a real bug: manually toggling WS_EX_LAYERED via raw
        // SetWindowLong on a live WPF HwndSource fights WPF's own transparency/composition
        // management (which expects to own that flag itself, via Window.AllowsTransparency,
        // not have it set externally) - the window would render fine initially, then go blank
        // the moment a click triggered a redraw. The window's own AllowsTransparency="True" in
        // XAML is what correctly handles real transparency here. WS_EX_TOOLWINDOW keeps the
        // window out of Alt+Tab and matches WindowEnumerator's own tool-window exclusion
        // filter, so a click-through widget can never become a ForceForeground/Activate
        // candidate.
        SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE,
            (int)(exStyle | WindowStylesEx.WS_EX_TRANSPARENT | WindowStylesEx.WS_EX_TOOLWINDOW));
    }

    public static void MakeClickThrough(this Window window) =>
        window.SourceInitialized += (_, _) =>
            Apply(new HWND(new WindowInteropHelper(window).Handle));
}
