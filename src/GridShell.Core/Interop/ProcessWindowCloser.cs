using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Finds/closes a process's top-level window(s) by PID. Closing uses a direct
/// WM_CLOSE — NOT Process.CloseMainWindow(), which silently does nothing for a window with
/// ShowInTaskbar="False" (sets WS_EX_TOOLWINDOW): .NET's MainWindowHandle resolution
/// explicitly skips tool windows, the same filter real taskbars use to exclude them.</summary>
public static class ProcessWindowCloser
{
    /// <summary>All top-level windows of the given process, visible or not — a pre-warmed,
    /// hidden window (e.g. GridShell.Grid before its first toggle-show) still needs to be
    /// addressable, so this intentionally doesn't filter by IsWindowVisible.</summary>
    public static List<HWND> FindWindowsOf(int processId)
    {
        var matches = new List<HWND>();

        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == (uint)processId)
            {
                matches.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        return matches;
    }

    public static bool CloseAnyWindowOf(int processId)
    {
        var matches = FindWindowsOf(processId);

        foreach (var hwnd in matches)
        {
            PostMessage(hwnd, (uint)WindowMessage.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        return matches.Count > 0;
    }
}
