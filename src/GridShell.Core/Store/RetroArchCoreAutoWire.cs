using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using GridShell.Core.Settings;
using GridShell.Core.Tiles;

namespace GridShell.Core.Store;

/// <summary>Downloads every RetroArchCoreCatalog core RetroArch doesn't already have, from
/// libretro's own buildbot (the same place RetroArch's in-app Online Updater itself pulls from),
/// then wires RetroArch as the emulator for each of those platforms - the same spirit as
/// EmulatorAutoWire, but per-platform argument templates (each needs its own "-L &lt;core&gt;"
/// flag) don't fit that one's one-exe-one-template model, so this is its own pass. Never
/// overwrites a slug the user (or EmulatorAutoWire, for a platform with its own dedicated Store
/// emulator) already configured. URL pattern and zip layout (one bare .dll per zip, no
/// subfolder) confirmed live against the real buildbot before writing this.</summary>
public static class RetroArchCoreAutoWire
{
    private const string BuildbotBaseUrl = "http://buildbot.libretro.com/nightly/windows/x86_64/latest/";

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("GridShell", "1.0"));
        return client;
    }

    /// <summary>Call when there's no already-open AppSettings to mutate in place (e.g. right
    /// after installing RetroArch from the Store) - loads/saves its own copy, same convention
    /// as EmulatorAutoWire.</summary>
    public static async Task TryWireAllAsync()
    {
        var settings = AppSettingsStore.Load();
        if (await TryWireAllAsync(settings))
        {
            AppSettingsStore.Save(settings);
        }
    }

    /// <summary>Call when a window already has a live AppSettings it wants kept in sync (Game
    /// Manager) - mutates it directly instead of risking a stale reload clobbering the window's
    /// own unsaved in-memory state. Returns whether anything changed, so the caller can decide
    /// whether/how to persist.</summary>
    public static async Task<bool> TryWireAllAsync(AppSettings settings)
    {
        var retroArchExe = InstalledAppLocator.FindMainExe("RetroArch");
        if (retroArchExe is null)
        {
            return false;
        }

        var retroArchDir = Path.GetDirectoryName(retroArchExe) ?? "";
        var coresDir = Path.Combine(retroArchDir, "cores");
        Directory.CreateDirectory(coresDir);

        SetSystemDirectory(retroArchDir);

        var changed = false;

        foreach (var platform in EmulationPlatforms.All)
        {
            if (settings.EmulatorConfigs.ContainsKey(platform.Slug))
            {
                continue;
            }

            var coreFileName = RetroArchCoreCatalog.GetCoreFileName(platform.Slug);
            if (coreFileName is null)
            {
                continue;
            }

            var corePath = Path.Combine(coresDir, coreFileName);
            if (!File.Exists(corePath) && !await TryDownloadCoreAsync(coreFileName, corePath))
            {
                continue;
            }

            settings.EmulatorConfigs[platform.Slug] = new EmulatorConfig
            {
                EmulatorPath = retroArchExe,
                ArgumentTemplate = $"-f -L \"{corePath}\" \"{{rom}}\"",
            };
            changed = true;
        }

        return changed;
    }

    /// <summary>Points RetroArch's own "System/BIOS Directory" setting (retroarch.cfg's
    /// system_directory key, confirmed live by launching a fresh RetroArch install briefly to
    /// let it generate its default config, then reading the key it wrote) at
    /// EmulationPlatforms.BiosRootPath itself (not the RetroArch subfolder below) - the user's
    /// own confirmed knowledge is that RetroArch's BIOS lookup is recursive, so pointing at the
    /// shared root already covers a RetroArch subfolder (or any other emulator's own) without
    /// needing a RetroArch-specific path. retroarch.cfg lives right next to the exe on this
    /// portable-style install (confirmed the same way) - if it doesn't exist yet (RetroArch has
    /// never actually been launched), writes a minimal one with just this key; RetroArch fills
    /// in every other default itself on first real launch, the same as it would for any config
    /// missing a key.</summary>
    private static void SetSystemDirectory(string retroArchDir)
    {
        Directory.CreateDirectory(EmulationPlatforms.BiosRootPath);

        // Just a pre-made, conventionally-named spot to drop RetroArch-specific BIOS files in -
        // not required (system_directory stays pointed at the shared root above, not this
        // subfolder), since BIOS lookup there is recursive.
        Directory.CreateDirectory(Path.Combine(EmulationPlatforms.BiosRootPath, "RetroArch"));

        var cfgPath = Path.Combine(retroArchDir, "retroarch.cfg");
        var desiredLine = $"system_directory = \"{EmulationPlatforms.BiosRootPath}\"";

        if (!File.Exists(cfgPath))
        {
            File.WriteAllText(cfgPath, desiredLine + Environment.NewLine);
            return;
        }

        var lines = File.ReadAllLines(cfgPath);
        var lineIndex = Array.FindIndex(lines, l => l.TrimStart().StartsWith("system_directory", StringComparison.Ordinal));

        if (lineIndex < 0)
        {
            File.AppendAllText(cfgPath, Environment.NewLine + desiredLine + Environment.NewLine);
            return;
        }

        if (lines[lineIndex] == desiredLine)
        {
            return;
        }

        lines[lineIndex] = desiredLine;
        File.WriteAllLines(cfgPath, lines);
    }

    private static async Task<bool> TryDownloadCoreAsync(string coreFileName, string destinationPath)
    {
        var zipPath = Path.Combine(Path.GetTempPath(), $"gridshell-core-{Guid.NewGuid():N}.zip");
        try
        {
            var bytes = await Http.GetByteArrayAsync($"{BuildbotBaseUrl}{coreFileName}.zip");
            await File.WriteAllBytesAsync(zipPath, bytes);

            using var archive = ZipFile.OpenRead(zipPath);
            var entry = archive.GetEntry(coreFileName);
            if (entry is null)
            {
                return false;
            }

            entry.ExtractToFile(destinationPath, overwrite: true);
            return true;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // A core the buildbot doesn't actually have, or a transient network failure - leave
            // that one platform unconfigured rather than failing the whole pass.
            return false;
        }
        finally
        {
            File.Delete(zipPath);
        }
    }
}
