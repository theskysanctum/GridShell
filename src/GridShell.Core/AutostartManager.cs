using System.IO;
using Vanara.Windows.Shell;

namespace GridShell.Core;

/// <summary>Toggles launching GridShell at login via a shortcut in the user's Startup folder.</summary>
public static class AutostartManager
{
    private static readonly string ShortcutPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.Startup),
        "GridShell.lnk");

    public static bool IsEnabled => File.Exists(ShortcutPath);

    public static void Enable(string exePath)
    {
        using var link = new ShellLink(exePath, "", description: "GridShell");
        link.SaveAs(ShortcutPath);
    }

    public static void Disable()
    {
        if (File.Exists(ShortcutPath))
        {
            File.Delete(ShortcutPath);
        }
    }
}
