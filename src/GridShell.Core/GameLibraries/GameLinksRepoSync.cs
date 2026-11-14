using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using LibGit2Sharp;

namespace GridShell.Core.GameLibraries;

/// <summary>One distinct game within a slug's JSON - Mirrors holds every URL that repo listed
/// under this exact same name (confirmed against a real repo, darksunofsatan/obscure-softs: a
/// mirror is a byte-identical row repeated with a different host, e.g. two vimm.net links for
/// the same "Ace_Combat_2.7z" - never multi-part/multi-disc, so any single mirror is the whole
/// game). SizeDisplay is whichever mirror row actually had one, since several real entries
/// leave it blank.</summary>
public sealed record GameLinkEntry(string Name, List<string> Mirrors, string SizeDisplay);

/// <summary>Clones a user-chosen git repo (AppSettings.GameLinksRepoUrl) with one
/// &lt;platform-slug&gt;.json file per EmulationPlatform sitting at its root - each a JSON array
/// of [name, url, sizeDisplay] entries - so the Store's "Games" category can list and download
/// real game files for any platform the repo covers. Same shallow re-clone-on-open pattern
/// StoreCatalogSync already uses for GridShell's own app catalog, just pointed at whatever repo
/// the user configures rather than one fixed URL.</summary>
public static class GameLinksRepoSync
{
    private static readonly string LocalPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GridShell", "GameLinksRepoCache");

    /// <summary>Re-clones fresh on every call, same reasoning (and the same real staleness bug
    /// it works around) as StoreCatalogSync.SyncAndLoadAsync's own remarks. Returns false only
    /// when cloning failed AND there's nothing already cached to fall back to - a transient
    /// network blip against an otherwise-already-synced repo still leaves GetEntries working off
    /// the stale-but-present copy.</summary>
    public static async Task<bool> SyncAsync(string repoUrl)
    {
        try
        {
            await Task.Run(() => Sync(repoUrl));
            return true;
        }
        catch (Exception ex) when (ex is LibGit2SharpException or IOException or UnauthorizedAccessException)
        {
            return Directory.Exists(LocalPath);
        }
    }

    private static void Sync(string repoUrl)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(LocalPath)!);

        // Rename-then-delete rather than delete-in-place - see StoreCatalogSync.Sync's own
        // remarks for the live-reproduced UnauthorizedAccessException this sidesteps.
        if (Directory.Exists(LocalPath))
        {
            var staleBackup = LocalPath + "-stale-" + Guid.NewGuid().ToString("N");
            Directory.Move(LocalPath, staleBackup);
            TryDeleteBestEffort(staleBackup);
        }

        Repository.Clone(NormalizeUrl(repoUrl), LocalPath, new CloneOptions
        {
            FetchOptions = { Depth = 1 },
        });
    }

    private static void TryDeleteBestEffort(string path)
    {
        try
        {
            Directory.Delete(path, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Harmless clutter left under LocalAppData if something still has a file open.
        }
    }

    /// <summary>Accepts either a full git clone URL or a bare "owner/repo" GitHub shorthand -
    /// resolved against github.com, since anyone pointing at a different host just pastes its
    /// full clone URL instead.</summary>
    private static string NormalizeUrl(string repoUrl) =>
        repoUrl.Contains("://") || repoUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
            ? repoUrl
            : $"https://github.com/{repoUrl}.git";

    /// <summary>One slug's listing, straight off whatever SyncAsync last cloned, grouped into one
    /// GameLinkEntry per distinct name (see GameLinkEntry's own remarks on why that grouping is
    /// safe) - an empty list if that slug has no JSON file in the repo, nothing has been synced
    /// yet, or the file is malformed. Never throws.</summary>
    public static List<GameLinkEntry> GetEntries(string slug)
    {
        var path = Path.Combine(LocalPath, $"{slug}.json");
        if (!File.Exists(path))
        {
            return new List<GameLinkEntry>();
        }

        try
        {
            var json = File.ReadAllText(path);
            using var doc = JsonDocument.Parse(json);

            var rows = new List<(string Name, string Url, string Size)>();
            foreach (var row in doc.RootElement.EnumerateArray())
            {
                if (row.ValueKind != JsonValueKind.Array || row.GetArrayLength() < 2)
                {
                    continue;
                }

                var name = row[0].GetString() ?? "";
                var url = row[1].GetString() ?? "";
                var size = row.GetArrayLength() > 2 ? row[2].GetString() ?? "" : "";
                if (name.Length > 0 && url.Length > 0)
                {
                    rows.Add((name, url, size));
                }
            }

            // Preserves the JSON's own first-seen order rather than an alphabetical re-sort -
            // callers (the Store's Games category) already sort for display if they want to.
            return rows
                .GroupBy(r => r.Name, StringComparer.Ordinal)
                .Select(g => new GameLinkEntry(
                    g.Key,
                    g.Select(r => r.Url).Distinct().ToList(),
                    g.Select(r => r.Size).FirstOrDefault(s => s.Length > 0) ?? ""))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return new List<GameLinkEntry>();
        }
    }
}
