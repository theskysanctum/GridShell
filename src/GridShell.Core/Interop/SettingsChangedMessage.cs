using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>The custom window message Settings (running inside GridShell.Grid) posts to the
/// taskbar process whenever a setting changes, so the taskbar — a separate, always-running
/// process with its own copy of AppSettings loaded at startup — can reload and re-apply it
/// live (e.g. a theme change) instead of only taking effect on its next restart.</summary>
public static class SettingsChangedMessage
{
    public static readonly uint Id = RegisterWindowMessage("GridShellSettingsChanged");

    /// <summary>Posts this message to every live GridShell (taskbar process) window - shared by
    /// SettingsWindow and StoreWindow (Scenes), the two places outside the taskbar process itself
    /// that change AppSettings fields the taskbar needs to react to live.</summary>
    public static void NotifyTaskbar()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("GridShell"))
        {
            foreach (var hwnd in ProcessWindowCloser.FindWindowsOf(process.Id))
            {
                PostMessage(hwnd, Id, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }
}
