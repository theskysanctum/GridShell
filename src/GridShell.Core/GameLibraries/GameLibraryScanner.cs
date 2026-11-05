namespace GridShell.Core.GameLibraries;

/// <summary>Scans every supported third-party launcher for already-installed games. itch.io is
/// deliberately not included - its own integration docs (itch.io/docs/butler/launcher-
/// integration) say a launcher is supposed to talk to its background "butlerd" daemon over
/// JSON-RPC rather than read its files directly, and that daemon protocol isn't documented
/// well enough to implement confidently here.</summary>
public static class GameLibraryScanner
{
    public static List<InstalledGame> ScanAll()
    {
        var results = new List<InstalledGame>();

        TryScan(() => results.AddRange(SteamLibraryScanner.Scan()));
        TryScan(() => results.AddRange(EpicLibraryScanner.Scan()));
        TryScan(() => results.AddRange(GogLibraryScanner.Scan()));

        return results;
    }

    // One launcher's registry/file layout being unexpectedly absent or inaccessible shouldn't
    // stop the others from being scanned.
    private static void TryScan(Action scan)
    {
        try
        {
            scan();
        }
        catch (Exception ex) when (ex is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
        }
    }
}
