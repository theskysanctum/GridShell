using System.Text;
using Vanara.PInvoke;
using static Vanara.PInvoke.DwmApi;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Enumerates top-level windows eligible for a taskbar window list, using the same
/// checks real taskbar replacements use (visible, not a tool window, not owned, has a title,
/// not DWM-cloaked, not one of GridShell's own windows).</summary>
public static class WindowEnumerator
{
    // Ambient system UI processes that can produce an eligible-looking top-level window but
    // aren't real apps a user would want to switch to — e.g. "Windows Input Experience" (the
    // touch keyboard/handwriting panel — TextInputHost on current Windows builds, TabTip on
    // older ones), both of which Windows can keep pre-launched/cached in the background
    // without the user opening them (seen showing up in the window list unprompted).
    //
    // SystemSettings is the Windows Settings app — confirmed via live logging that on this
    // Windows build it runs as its own top-level process named exactly "SystemSettings", not
    // hosted inside the generic ApplicationFrameHost the way UWP apps traditionally are (an
    // earlier version of this exclusion assumed ApplicationFrameHost hosting and matched on the
    // window's exact title instead — kept below as a fallback for builds where that's still
    // true, but it no longer catches Settings on its own here).
    private static readonly HashSet<string> ExcludedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "TextInputHost", "TabTip", "TabTip32", "SystemSettings",
    };

    private const string ApplicationFrameHostProcessName = "ApplicationFrameHost";
    private const string WindowsSettingsTitle = "Settings";

    public static List<HWND> EnumerateEligibleWindows()
    {
        var result = new List<HWND>();

        EnumWindows((hwnd, _) =>
        {
            if (IsEligible(hwnd))
            {
                result.Add(hwnd);
            }

            return true;
        }, IntPtr.Zero);

        return result;
    }

    public static bool IsEligible(HWND hwnd)
    {
        try
        {
            if (!IsWindowVisible(hwnd))
            {
                return false;
            }

            var exStyle = (WindowStylesEx)GetWindowLong(hwnd, WindowLongFlags.GWL_EXSTYLE);
            var isToolWindow = exStyle.HasFlag(WindowStylesEx.WS_EX_TOOLWINDOW);
            var isAppWindow = exStyle.HasFlag(WindowStylesEx.WS_EX_APPWINDOW);

            if (isToolWindow && !isAppWindow)
            {
                return false;
            }

            var owner = GetWindow(hwnd, GetWindowCmd.GW_OWNER);
            if (!owner.IsNull && !isAppWindow)
            {
                return false;
            }

            var titleLength = GetWindowTextLength(hwnd);
            if (titleLength == 0)
            {
                return false;
            }

            // DwmGetWindowAttribute can fail for windows owned by a different integrity level
            // (elevated apps, some system processes) — treat a failed query as "not cloaked"
            // rather than silently excluding the window entirely.
            try
            {
                DwmGetWindowAttribute<int>(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, out var cloaked);
                if (cloaked != 0)
                {
                    return false;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }

            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == (uint)Environment.ProcessId)
            {
                return false;
            }

            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                if (ExcludedProcessNames.Contains(process.ProcessName))
                {
                    return false;
                }

                if (string.Equals(process.ProcessName, ApplicationFrameHostProcessName, StringComparison.OrdinalIgnoreCase))
                {
                    var sb = new StringBuilder(titleLength + 1);
                    GetWindowText(hwnd, sb, sb.Capacity);
                    if (string.Equals(sb.ToString(), WindowsSettingsTitle, StringComparison.Ordinal))
                    {
                        return false;
                    }
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException) { }

            return true;
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // A single misbehaving window shouldn't take down the whole taskbar.
            return false;
        }
    }

    /// <summary>Restores (if minimized) and foregrounds a window — shared by the taskbar's
    /// click-to-switch and single-instance tile launching.</summary>
    public static void Activate(HWND hwnd)
    {
        if (IsIconic(hwnd))
        {
            ShowWindow(hwnd, ShowWindowCommand.SW_RESTORE);
        }

        ForceForeground(hwnd);
    }

    /// <summary>Same as Activate, but also ensures the window is maximized — used when
    /// reactivating something GridShell itself launched, which should always stay maximized
    /// rather than just restoring whatever (possibly smaller) state it was last left in.</summary>
    public static void ActivateMaximized(HWND hwnd)
    {
        if (!IsZoomed(hwnd))
        {
            ShowWindow(hwnd, ShowWindowCommand.SW_MAXIMIZE);
        }

        ForceForeground(hwnd);
    }

    /// <summary>A plain SetForegroundWindow call is silently ignored by Windows' foreground-lock
    /// rules when it comes from a background process (confirmed by real testing - a freshly
    /// launched game/emulator needed a manual click to actually get focus). Combines the two
    /// standard workarounds rather than relying on either alone: a synthetic Alt key tap (real
    /// input resets Windows' "is the user actively switching windows" state, which is what the
    /// lock actually keys off) and briefly attaching this thread's input to both the current
    /// foreground window's thread and the target's thread (which makes Windows treat the call
    /// as "switching within the same input context"). SetForegroundWindow's own return value
    /// isn't trustworthy enough on its own to skip either one.</summary>
    public static void ForceForeground(HWND hwnd)
    {
        SyntheticKeyboard.PressKey(VK.VK_MENU);

        var foregroundHwnd = GetForegroundWindow();
        var currentThreadId = Kernel32.GetCurrentThreadId();
        var foregroundThreadId = GetWindowThreadProcessId(foregroundHwnd, out _);
        var targetThreadId = GetWindowThreadProcessId(hwnd, out _);

        var attachedToForeground = foregroundThreadId != currentThreadId
            && AttachThreadInput(currentThreadId, foregroundThreadId, true);
        var attachedToTarget = targetThreadId != currentThreadId
            && AttachThreadInput(currentThreadId, targetThreadId, true);

        try
        {
            if (IsIconic(hwnd))
            {
                ShowWindow(hwnd, ShowWindowCommand.SW_RESTORE);
            }

            SetForegroundWindow(hwnd);
            BringWindowToTop(hwnd);
            SetActiveWindow(hwnd);
        }
        finally
        {
            if (attachedToForeground)
            {
                AttachThreadInput(currentThreadId, foregroundThreadId, false);
            }

            if (attachedToTarget)
            {
                AttachThreadInput(currentThreadId, targetThreadId, false);
            }
        }
    }

    /// <summary>Finds an eligible window belonging to a process running from the given
    /// executable path, if any. When arguments is given, also requires the process's own
    /// command line to contain it — needed for e.g. multiple ROMs launched through the same
    /// shared emulator executable, which a bare exe-path match can't tell apart (every one of
    /// them would otherwise "already be running" as far as this is concerned).</summary>
    public static HWND? FindWindowForExecutable(string exePath, string? arguments = null)
    {
        foreach (var hwnd in EnumerateEligibleWindows())
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            try
            {
                using var process = System.Diagnostics.Process.GetProcessById((int)pid);
                var processPath = process.MainModule?.FileName;
                if (processPath is null || !string.Equals(processPath, exePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                if (string.IsNullOrEmpty(arguments) || CommandLineContains((int)pid, arguments))
                {
                    return hwnd;
                }
            }
            catch (Exception ex) when (ex is not OutOfMemoryException)
            {
                // Process may have exited, or belongs to a different privilege level we can't inspect.
            }
        }

        return null;
    }

    private static bool CommandLineContains(int pid, string needle)
    {
        try
        {
            using var searcher = new System.Management.ManagementObjectSearcher(
                $"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {pid}");
            foreach (System.Management.ManagementBaseObject result in searcher.Get())
            {
                if (result["CommandLine"] is string commandLine &&
                    commandLine.Contains(needle, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            // WMI unavailable/restricted — treat as no match rather than failing the whole lookup.
        }

        return false;
    }

    /// <summary>Finds an eligible window belonging to the given process ID, if any.</summary>
    public static HWND? FindWindowForProcessId(int processId)
    {
        foreach (var hwnd in EnumerateEligibleWindows())
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == (uint)processId)
            {
                return hwnd;
            }
        }

        return null;
    }
}
