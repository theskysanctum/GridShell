using System.IO;

namespace GridShell.Core.Interop;

/// <summary>Derives a sibling project's exe path from the CURRENT process's own exe location.
/// Walks up from the exe's folder until it finds the project folder itself (the directory whose
/// parent is named "src"), then reapplies whatever relative path remains — bin\Release\&lt;TFM&gt;\
/// today, bin\Release\&lt;TFM&gt;\win-x64\ under a self-contained publish — against the sibling
/// project folder. Self-locating rather than a fixed depth, so it survives an extra RID folder
/// level that a hardcoded "three levels up" does not. The same derivation GridLauncher and
/// GridShell.Watchdog each need for a different sibling, pulled out once instead of copied a
/// third time.</summary>
public static class SiblingExePath
{
    public static string Resolve(string siblingProjectName, string siblingExeName)
    {
        var ownExeDir = new DirectoryInfo(Path.GetDirectoryName(Environment.ProcessPath)!);
        var projectDir = ownExeDir;
        while (projectDir.Parent is not null && projectDir.Parent.Name != "src")
        {
            projectDir = projectDir.Parent;
        }

        if (projectDir.Parent is null)
        {
            throw new InvalidOperationException($"Could not locate a 'src' ancestor above '{ownExeDir.FullName}'.");
        }

        var srcDir = projectDir.Parent;
        var relativeBinPath = Path.GetRelativePath(projectDir.FullName, ownExeDir.FullName);
        return Path.Combine(srcDir.FullName, siblingProjectName, relativeBinPath, siblingExeName);
    }
}
