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
        // directly instead of the registry scan winget-installed apps need.
        var exePath = app.InstallMethod == "github"
            ? InstalledAppLocator.FindBestExeInDirectory(GitHubReleaseInstaller.InstallRoot(app.Id))
            : InstalledAppLocator.FindMainExe(app.Name);
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
