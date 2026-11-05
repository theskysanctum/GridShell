using System.Text;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Hides/shows the real Windows taskbar's own window(s) via SetWindowPos+SWP_HIDEWINDOW
/// — without touching the registered shell.</summary>
public static class ShellTaskbarVisibility
{
    public static void Hide() => SetVisible(false);

    public static void Show() => SetVisible(true);

    private static void SetVisible(bool visible)
    {
        const SetWindowPosFlags baseFlags =
            SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE |
            SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE;

        var flag = visible ? SetWindowPosFlags.SWP_SHOWWINDOW : SetWindowPosFlags.SWP_HIDEWINDOW;
        foreach (var hwnd in FindTaskbarWindows())
        {
            SetWindowPos(hwnd, HWND.NULL, 0, 0, 0, 0, baseFlags | flag);
        }
    }

    private static List<HWND> FindTaskbarWindows()
    {
        var result = new List<HWND>();

        EnumWindows((hwnd, _) =>
        {
            var className = GetClassName(hwnd);
            if (className is "Shell_TrayWnd" or "Shell_SecondaryTrayWnd")
            {
                result.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    private static string GetClassName(HWND hwnd)
    {
        var sb = new StringBuilder(256);
        User32.GetClassName(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
