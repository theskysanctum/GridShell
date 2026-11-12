using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Posted to GridShell.exe's taskbar window right before it's asked to close as part of
/// installing an update - tells it to exit with <see cref="ShellExitCodes.Updating"/> instead of
/// the usual <see cref="ShellExitCodes.RestoreExplorer"/>, so GridShell.Watchdog (if running)
/// knows not to relaunch it mid-install.</summary>
public static class PrepareForUpdateMessage
{
    public static readonly uint Id = RegisterWindowMessage("GridShellPrepareForUpdate");
}
