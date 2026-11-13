using System.IO;
using GridShell.Core.Settings;
using GridShell.Core.Tiles;

namespace GridShell.Core.Store;

/// <summary>After WingetInstaller reports an app installed (or already installed), finds its
/// real exe (via InstalledAppLocator) and - if the catalog entry lists platform slugs - fills in
/// Settings' EmulatorConfigs for them, so an emulator installed through the Store is actually
/// usable immediately, not just present on disk. Never overwrites a slug the user already
/// configured themselves.</summary>
public static class EmulatorAutoWire
{
    public static void TryWire(StoreApp app)
    {
        if (app.PlatformSlugs.Count == 0)
        {
            return;
        }

        // GitHub-release installs (GitHubReleaseInstaller) extract straight into a fixed folder
        // of GridShell's own choosing and never touch the Uninstall registry - look there
        // directly instead of the registry scan winget-installed apps need. The registry scan
        // itself can still come back empty for a real winget install - confirmed live: 7-Zip's
        // own MSI leaves both InstallLocation and DisplayIcon blank in its Uninstall entry - so
        // this falls back to the exact folder WingetInstaller's own --location flag requested,
        // which winget actually installs to regardless of what it reports back to the registry.
        var exePath = app.InstallMethod == "github"
            ? InstalledAppLocator.FindBestExeInDirectory(GitHubReleaseInstaller.InstallRoot(app.Id), app.Name)
            : InstalledAppLocator.FindMainExe(app.Name)
              ?? InstalledAppLocator.FindBestExeInDirectory(Path.Combine(WingetInstaller.InstallRoot, app.Id), app.Name);
        if (exePath is null)
        {
            return;
        }

        EmulatorBiosAutoWire.TryWire(app.Id, exePath);

        var settings = AppSettingsStore.Load();
        var changed = false;

        foreach (var slug in app.PlatformSlugs)
        {
            if (settings.EmulatorConfigs.ContainsKey(slug))
            {
                continue;
            }

            settings.EmulatorConfigs[slug] = new EmulatorConfig { EmulatorPath = exePath };
            changed = true;
        }

        if (changed)
        {
            AppSettingsStore.Save(settings);
        }
    }
}
