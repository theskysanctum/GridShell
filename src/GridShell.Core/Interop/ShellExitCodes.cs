namespace GridShell.Core.Interop;

/// <summary>Process exit codes GridShell.Watchdog uses to decide what to do when GridShell.exe
/// exits while registered as the Windows shell: RestoreExplorer means "the user deliberately
/// exited — launch Explorer and stop watching", Updating means "an update installer was just
/// launched — just stop watching, the installer's own post-install step relaunches GridShell",
/// anything else means "unexpected — relaunch GridShell, bounded, falling back to Explorer if
/// it keeps happening".</summary>
public static class ShellExitCodes
{
    public const int RestoreExplorer = 99;
    public const int Updating = 98;
}
