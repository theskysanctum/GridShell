using System.Diagnostics;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Whether the on-screen keyboard is currently visible, checked from the taskbar
/// (GridShell) process even though the window itself lives in GridShell.Grid - there's no
/// existing cross-process "OSK visibility changed" message (OpenKeyboardMessage only carries
/// the toggle request, one-way, Grid-ward), so this looks the window up directly by its known
/// Title ("Keyboard", OnScreenKeyboardWindow.xaml) and confirms it actually belongs to
/// GridShell.Grid.exe before trusting it, rather than matching on title text alone.</summary>
public static class OnScreenKeyboardState
{
    public static bool IsVisible()
    {
        var hwnd = FindWindow(null, "Keyboard");
        if (hwnd.IsNull)
        {
            return false;
        }

        GetWindowThreadProcessId(hwnd, out var pid);

        try
        {
            using var process = Process.GetProcessById((int)pid);
            if (!string.Equals(process.ProcessName, "GridShell.Grid", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return false;
        }

        return IsWindowVisible(hwnd);
    }
}
