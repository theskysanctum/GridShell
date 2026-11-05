using System.Diagnostics;
using Vanara.PInvoke;
using static Vanara.PInvoke.AdvApi32;
using static Vanara.PInvoke.PowrProf;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Session/power actions GridShell's own UI needs to offer once it's the registered
/// shell — Explorer normally provides these (Start menu's power button, Ctrl+Alt+Del's own
/// options still work independently either way, but having them in GridShell itself avoids
/// relying on that as the only way out).</summary>
public static class PowerActions
{
    public static void Lock() => LockWorkStation();

    public static void SignOut() => ExitWindowsEx(ExitWindowsFlags.EWX_LOGOFF, 0);

    public static void Sleep() => SetSuspendState(false, false, false);

    public static void Restart()
    {
        EnableShutdownPrivilege();
        ExitWindowsEx(ExitWindowsFlags.EWX_REBOOT, 0);
    }

    public static void ShutDown()
    {
        EnableShutdownPrivilege();
        ExitWindowsEx(ExitWindowsFlags.EWX_SHUTDOWN, 0);
    }

    /// <summary>Restart/shut down (unlike sign-out/lock) require SE_SHUTDOWN_NAME enabled on the
    /// calling process's token — not granted by default.</summary>
    private static void EnableShutdownPrivilege()
    {
        using var token = SafeHTOKEN.FromProcess(Process.GetCurrentProcess(), TokenAccess.TOKEN_ADJUST_PRIVILEGES | TokenAccess.TOKEN_QUERY);
        LookupPrivilegeValue(null, "SeShutdownPrivilege", out LUID luid);
        var newState = new TOKEN_PRIVILEGES(luid, PrivilegeAttributes.SE_PRIVILEGE_ENABLED);
        token.AdjustPrivileges(false, in newState, out _);
    }
}
