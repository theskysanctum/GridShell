using System.Diagnostics;
using GridShell.Core.Interop;

// Only meaningful when GridShell is registered as the Windows shell — this is what Winlogon
// actually launches in that case. Keeps GridShell.exe running, relaunching it (bounded) if it
// crashes, and falls back to explorer.exe either when GridShell exits deliberately
// (ShellExitCodes.RestoreExplorer) or when it keeps crashing faster than that bound allows.
const int maxRelaunchAttempts = 3;
var relaunchWindow = TimeSpan.FromSeconds(60);
var recentLaunches = new List<DateTime>();

var gridShellExePath = GetGridShellExePath();

while (true)
{
    recentLaunches.Add(DateTime.UtcNow);
    recentLaunches.RemoveAll(t => DateTime.UtcNow - t > relaunchWindow);

    if (recentLaunches.Count > maxRelaunchAttempts)
    {
        FallBackToExplorer();
        return;
    }

    Process process;
    try
    {
        process = Process.Start(gridShellExePath);
    }
    catch (System.ComponentModel.Win32Exception)
    {
        // GridShell.exe itself couldn't even be started — no point retrying that.
        FallBackToExplorer();
        return;
    }

    process.WaitForExit();

    if (process.ExitCode == ShellExitCodes.RestoreExplorer)
    {
        FallBackToExplorer();
        return;
    }

    if (process.ExitCode == ShellExitCodes.Updating)
    {
        // An update installer was just launched and is about to overwrite these very files -
        // stop watching entirely rather than relaunching (would race the installer) or falling
        // back to Explorer (the installer's own [Run] step relaunches GridShell once it's done).
        return;
    }

    // Any other exit code (crash, killed, etc.) — loop around and relaunch, bounded by the
    // check above.
}

static void FallBackToExplorer()
{
    try
    {
        Process.Start("explorer.exe");
    }
    catch (Exception ex) when (ex is not OutOfMemoryException)
    {
        // Nothing further we can do from here.
    }
}

static string GetGridShellExePath() => SiblingExePath.Resolve("GridShell", "GridShell.exe");
