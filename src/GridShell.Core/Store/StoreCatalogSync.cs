using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace GridShell.Core.Store;

/// <summary>Keeps a local clone of GridShell's own repo around just to read
/// store-catalog/catalog.json from it - lets the curated app list be updated by pushing to that
/// one file, with no GridShell release needed. WinGet/GitHubReleaseInstaller/PackageInstaller
/// actually install software; this just supplies what to list. Scenes and "package"-method
/// installer files (see StoreSceneSync/PackageInstaller) deliberately don't go through this
/// clone - they're fetched directly over plain HTTP instead, on demand, so a Store open never
/// pulls down a multi-MB installer archive just to show its Install button.</summary>
public static class StoreCatalogSync
{
    private const string RepoUrl = "https://github.com/theskysanctum/gridshell.git";
    private const string Branch = "master";
    private const string CatalogRelativePath = "store-catalog/catalog.json";
    private const string IconsRelativeDir = "store-catalog/icons";

    private static readonly string LocalPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GridShell", "StoreCatalogCache");

    /// <summary>Re-clones the cache fresh on every call, then reads the catalog from it. Falls
    /// back to whatever's already on disk - stale is better than empty - if syncing fails (no
    /// internet, etc.), and to a genuinely empty catalog only if nothing has ever been synced
    /// successfully.
    ///
    /// Deliberately re-clones rather than fetch+reset on an existing checkout: tried that first,
    /// and on this real repo it left HEAD stuck on the commit from the PREVIOUS clone even
    /// though `git fetch` on the same checkout (plain git, not LibGit2Sharp) picked up the new
    /// commit just fine - a real, reproduced staleness bug in that path, not a guess. The repo is
    /// small (~14 MB .git), so a fresh clone each time the Store opens is cheap enough to just
    /// sidestep the whole class of problem.</summary>
    public static async Task<StoreCatalogFile> SyncAndLoadAsync()
    {
        try
        {
            await Task.Run(Sync);
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            // Network/git failure, or the previous clone's files still being locked (observed
            // live: deleting the old clone before re-cloning threw UnauthorizedAccessException -
            // "Access to the path ... is denied" - with no read-only attribute and a normal ACL,
            // consistent with something (antivirus real-time scan, most likely) transiently
            // holding one of the just-written .git object files open) - fall through to
            // whatever's already cached, if anything, rather than crashing the Store window.
        }

        return Load();
    }

    private static void Sync()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LocalPath)!);

        // Rename the old clone out of the way rather than deleting it in place first: deleting
        // was observed live to throw UnauthorizedAccessException ("Access to the path ... is
        // denied") on a freshly-written .git object file with a normal ACL and no read-only
        // attribute - consistent with something (most likely antivirus real-time scanning)
        // transiently holding it open - and a delete that fails partway through leaves a
        // corrupted clone (confirmed live: catalog.json already gone) rather than either the old
        // or new catalog intact. A rename isn't blocked by that same transient lock (confirmed
        // live against the same locked file) since it only changes the directory entry.
        if (Directory.Exists(LocalPath))
        {
            var staleBackup = LocalPath + "-stale-" + Guid.NewGuid().ToString("N");
            Directory.Move(LocalPath, staleBackup);
            TryDeleteBestEffort(staleBackup);
        }

        Repository.Clone(RepoUrl, LocalPath);
    }

    private static void TryDeleteBestEffort(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Harmless clutter left under LocalAppData if something still has a file open -
            // not worth failing the sync over.
        }
    }

    /// <summary>Full path to a catalog app's bundled icon (store-catalog/icons/{id}.png in the
    /// synced clone), or null if that app has none yet - the UI falls back to its
    /// initial-letter square in that case.</summary>
    public static string? GetIconPath(string appId)
    {
        var path = Path.Combine(LocalPath, IconsRelativeDir, $"{appId}.png");
        return File.Exists(path) ? path : null;
    }

    private static StoreCatalogFile Load()
    {
        var path = Path.Combine(LocalPath, CatalogRelativePath);
        if (!File.Exists(path))
        {
            return new StoreCatalogFile();
        }

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<StoreCatalogFile>(json) ?? new StoreCatalogFile();
        }
        catch (JsonException)
        {
            // A syntax error in catalog.json (confirmed live: a bad hand-edit pushed to the
            // repo) used to propagate straight out of StoreWindow_Loaded's async void handler -
            // uncaught there, that's fatal to the whole process, not just a broken Store window.
            // Same "empty catalog rather than crash" fallback Sync()'s own catch already uses for
            // a failed clone.
            return new StoreCatalogFile();
        }
    }
}
