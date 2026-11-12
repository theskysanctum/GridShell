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

    /// <summary>Clones (first run) or fetches+hard-resets (every run after) the cache, then reads
    /// the catalog from it. Falls back to whatever's already on disk - stale is better than
    /// empty - if syncing fails (no internet, etc.), and to a genuinely empty catalog only if
    /// nothing has ever been synced successfully.</summary>
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
        if (!Directory.Exists(Path.Combine(LocalPath, ".git")))
        {
            if (Directory.Exists(LocalPath))
            {
                Directory.Delete(LocalPath, recursive: true);
            }

            Directory.CreateDirectory(Path.GetDirectoryName(LocalPath)!);
            Repository.Clone(RepoUrl, LocalPath);
            return;
        }

        using var repo = new Repository(LocalPath);
        var remote = repo.Network.Remotes.First(r => r.Name == "origin");
        var refspec = $"+refs/heads/{Branch}:refs/remotes/origin/{Branch}";
        repo.Network.Fetch(remote.Url, new[] { refspec });

        var tip = (Commit)repo.Lookup($"refs/remotes/origin/{Branch}");
        repo.Reset(ResetMode.Hard, tip);
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
