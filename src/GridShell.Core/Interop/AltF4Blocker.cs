namespace GridShell.Core.Interop;

/// <summary>Detects the Win32 message Alt+F4 (and the system-menu "Close" item) actually sends —
/// WM_SYSCOMMAND with SC_CLOSE — so a window's WndProc hook can swallow just that, blocking the
/// user from closing it that way. This does NOT affect a window's own programmatic Close() or a
/// direct WM_CLOSE (e.g. from ProcessWindowCloser), which arrive as different messages entirely —
/// those still work normally for GridShell's own deliberate shutdown paths (the Settings "Exit"
/// button, process force-kill, etc.).</summary>
public static class AltF4Blocker
{
    private const int WM_SYSCOMMAND = 0x0112;
    private const int SC_CLOSE = 0xF060;

    // The low 4 bits of a system command are reserved by Windows for its own use and must be
    // masked off before comparing against the SC_* constants.
    private const int SysCommandMask = 0xFFF0;

    public static bool IsSysCommandClose(int msg, IntPtr wParam) =>
        msg == WM_SYSCOMMAND && ((int)wParam & SysCommandMask) == SC_CLOSE;
}
