namespace GridShell.Core.Interop;

/// <summary>Process exit codes GridShell.Watchdog uses to decide what to do when GridShell.exe
/// exits while registered as the Windows shell: RestoreExplorer means "the user deliberately
/// exited — launch Explorer and stop watching", anything else means "unexpected — relaunch
/// GridShell, bounded, falling back to Explorer if it keeps happening".</summary>
public static class ShellExitCodes
{
    public const int RestoreExplorer = 99;
}
