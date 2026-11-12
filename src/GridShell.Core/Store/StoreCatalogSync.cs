using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace GridShell.Core.Store;

/// <summary>Keeps a local clone of GridShell's own repo around just to read
/// store-catalog/catalog.json from it - lets the curated app list be updated by pushing to that
/// one file, with no GridShell release needed. WinGet (see WingetInstaller) is the only thing
/// that actually installs software; this just supplies what to list.</summary>
public static class StoreCatalogSync
{
    private const string RepoUrl = "https://github.com/theskysanctum/gridshell.git";
    private const string Branch = "master";
    private const string CatalogRelativePath = "store-catalog/catalog.json";

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
        catch (Exception ex) when (ex is LibGit2SharpException or IOException)
        {
            // Network/git failure - fall through to whatever's already cached, if anything.
        }

        return Load();
    }

    private static void Sync()
    {
        if (Directory.Exists(LocalPath))
        {
            Directory.Delete(LocalPath, recursive: true);
        }

        Directory.CreateDirectory(Path.GetDirectoryName(LocalPath)!);
        Repository.Clone(RepoUrl, LocalPath);
    }

    private static StoreCatalogFile Load()
    {
        var path = Path.Combine(LocalPath, CatalogRelativePath);
        if (!File.Exists(path))
        {
            return new StoreCatalogFile();
        }

        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<StoreCatalogFile>(json) ?? new StoreCatalogFile();
    }
}
