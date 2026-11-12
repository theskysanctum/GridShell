using System.IO;
using System.Linq;

namespace GridShell.Core.Tiles;

/// <summary>Scans &lt;root&gt;\&lt;platform-slug&gt; recursively for ROM files matching that
/// platform's known extensions, across the default root
/// (<see cref="EmulationPlatforms.RomsRootPath"/>) plus any extra root folders configured in
/// Settings.</summary>
public static class RomScanner
{
    public static List<string> GetRoms(EmulationPlatform platform, IEnumerable<string>? extraRoots = null)
    {
        var roots = new[] { EmulationPlatforms.RomsRootPath }
            .Concat(extraRoots ?? Enumerable.Empty<string>());

        return roots
            .Select(root => Path.Combine(root, platform.Slug))
            .SelectMany(GetRomsInDirectory)
            .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        List<string> GetRomsInDirectory(string dir)
        {
            if (!Directory.Exists(dir))
            {
                return new List<string>();
            }

            try
            {
                var allFiles = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();

                // A multi-disc game ships one .m3u playlist (RetroArch's own convention,
                // listing each disc's real image filename) alongside the per-disc image files
                // themselves - without this, a 3-disc game would also match the platform's own
                // extensions 3 separate times, counting/listing as 3 "ROMs" instead of 1. .m3u
                // itself is always recognized here regardless of whether a platform happens to
                // list it, since it's a cross-platform convention, not an emulator-core format.
                var m3uFiles = allFiles
                    .Where(f => string.Equals(Path.GetExtension(f), ".m3u", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var discFilesCoveredByM3u = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var m3u in m3uFiles)
                {
                    foreach (var referencedPath in ReadM3uEntries(m3u, dir))
                    {
                        discFilesCoveredByM3u.Add(referencedPath);
                    }
                }

                var otherRoms = allFiles
                    .Where(f => platform.RomExtensions.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase))
                    .Where(f => !discFilesCoveredByM3u.Contains(f));

                return m3uFiles.Concat(otherRoms).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException or IOException)
            {
                return new List<string>();
            }
        }
    }

    /// <summary>Reads the disc filenames an .m3u playlist references, resolved to full paths
    /// within its own directory - skips blank lines and "#"-prefixed comments (e.g. the common
    /// "#EXTM3U" header line), same as RetroArch's own parser.</summary>
    private static IEnumerable<string> ReadM3uEntries(string m3uPath, string dir)
    {
        string[] lines;
        try
        {
            lines = File.ReadAllLines(m3uPath);
        }
        catch (IOException)
        {
            return Enumerable.Empty<string>();
        }

        return lines
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && line[0] != '#')
            .Select(line => Path.GetFullPath(Path.IsPathRooted(line) ? line : Path.Combine(dir, line)));
    }
}
