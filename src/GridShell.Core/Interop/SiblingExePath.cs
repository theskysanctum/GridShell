using System.IO;

namespace GridShell.Core.Interop;

/// <summary>Derives a sibling project's exe path from the CURRENT process's own exe location,
/// assuming the standard src/&lt;Project&gt;/bin/&lt;Config&gt;/&lt;TFM&gt;/ layout — the same
/// derivation GridLauncher and GridShell.Watchdog each need for a different sibling, pulled out
/// once instead of copied a third time.</summary>
public static class SiblingExePath
{
    public static string Resolve(string siblingProjectName, string siblingExeName)
    {
        var ownExeDir = Path.GetDirectoryName(Environment.ProcessPath)!;
        var ownProjectDir = Path.GetFullPath(Path.Combine(ownExeDir, "..", "..", ".."));
        var srcDir = Path.GetDirectoryName(ownProjectDir)!;
        var binConfigTfm = Path.GetRelativePath(ownProjectDir, ownExeDir);

        return Path.Combine(srcDir, siblingProjectName, binConfigTfm, siblingExeName);
    }
}
