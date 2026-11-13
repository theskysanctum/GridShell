using System.Diagnostics;

namespace GridShell.Core.Store;

/// <summary>Opens an app's official download page in the default browser and leaves the actual
/// install/uninstall entirely to the user - used for Steam, Heroic, and itch after enough
/// trouble automating their installs (--location breaking registry entries, winget's own stale
/// correlation cache, uninstall COM errors, elevation requirements DirectInstaller couldn't get
/// around). Detection still goes through the normal registry/exe lookup
/// (InstalledAppLocator.FindMainExe), same as every other install method - only the install/
/// uninstall ACTION is hands-off, not the Store's ability to recognize it's there.
///
/// Deliberately returns bool, not InstallResult/UninstallResult - opening a browser isn't an
/// install or uninstall outcome in the sense those types model (Installed/Failed don't apply;
/// there's no way to know whether the user actually did anything), so StoreWindow handles this
/// as its own case rather than forcing it through that result shape.</summary>
public static class BrowserInstaller
{
    public static bool IsInstalled(StoreApp app) => InstalledAppLocator.FindMainExe(app.Name) is not null;

    /// <summary>Returns false (with no exception) only if there was nothing to open at all -
    /// opening the browser itself is otherwise fire-and-forget, same as every other
    /// UseShellExecute launch in this codebase.</summary>
    public static bool Open(StoreApp app)
    {
        if (string.IsNullOrEmpty(app.BrowserUrl))
        {
            return false;
        }

        try
        {
            Process.Start(new ProcessStartInfo(app.BrowserUrl) { UseShellExecute = true });
            return true;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return false;
        }
    }
}
