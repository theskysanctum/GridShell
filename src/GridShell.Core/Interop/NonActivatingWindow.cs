using System.Windows;
using System.Windows.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Forces a window to never take keyboard/activation focus (WS_EX_NOACTIVATE) — used
/// for the on-screen keyboard, which must type into whatever app the user was already using
/// without ever stealing its focus (the same approach Windows' own on-screen keyboard uses).</summary>
public static class NonActivatingWindow
{
    public static void Apply(HWND hwnd)
    {
        var exStyle = (WindowStylesEx)GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);
        SetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE, (int)(exStyle | WindowStylesEx.WS_EX_NOACTIVATE));
    }

    public static void NeverActivate(this Window window)
    {
        window.SourceInitialized += (_, _) =>
            Apply(new WindowInteropHelper(window).Handle);
    }
}
