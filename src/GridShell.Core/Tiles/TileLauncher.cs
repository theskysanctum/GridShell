using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GridShell.Core.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Tiles;

/// <summary>Launches a tile's target the way the real Start menu would: honoring file-type
/// associations/verbs via the shell, not a bare CreateProcess. Every tile is single-instance —
/// if its target is already running, its window is activated instead of starting a duplicate.
/// Website tiles all share one browser executable and can't be told apart by process/PID either
/// — Chromium's own singleton behavior can route a "--app=&lt;url&gt;" launch for a brand new site
/// into an already-running browser process rather than starting a dedicated one (verified
/// empirically), so even the owning process's command line isn't reliable. Instead, each site's
/// window is tracked directly by watching for whatever new top-level window appears right after
/// launching it, independent of which process ends up owning it.</summary>
public static class TileLauncher
{
    private static readonly Dictionary<string, HWND> WebsiteTileWindows = new(StringComparer.OrdinalIgnoreCase);

    public static bool Launch(Tile tile)
    {
        if (tile.Kind == TileKind.Website)
        {
            return LaunchWebsite(tile);
        }

        // An "appx:" TargetPath resolves an MSIX-packaged app's exe via the package catalog
        // instead of a version-numbered path, so the tile keeps working across app updates.
        var targetPath = ResolveTargetPath(tile.TargetPath) ?? tile.TargetPath;

        if (WindowEnumerator.FindWindowForExecutable(targetPath, tile.Arguments) is { } existing)
        {
            // Games (emulators especially) manage their own fullscreen/windowed state — forcing
            // a native maximize on top of that fights it rather than helping. Apps still get the
            // existing "always maximized" treatment.
            if (tile.Kind == TileKind.Game)
            {
                WindowEnumerator.Activate(existing);
            }
            else
            {
                WindowEnumerator.ActivateMaximized(existing);
            }

            return true;
        }

        try
        {
            var before = new HashSet<HWND>(WindowEnumerator.EnumerateEligibleWindows());

            Process.Start(new ProcessStartInfo(targetPath)
            {
                Arguments = tile.Arguments ?? "",
                UseShellExecute = true,
                WindowStyle = tile.Kind == TileKind.Game ? ProcessWindowStyle.Normal : ProcessWindowStyle.Maximized,
            });

            // A game tile whose target IS one of the known PC game storefront launchers (Steam,
            // Heroic, itch) - e.g. Steam.exe with "-applaunch <id>" - actually starts that
            // launcher, not the real game; it hands off to/spawns the game itself. Once the
            // user's done, nothing closes the launcher on its own, so it's scoped exactly to
            // this: only a tile explicitly configured to launch through one of them, never any
            // launcher that merely happens to be running.
            var launcherToCloseAfter = tile.Kind == TileKind.Game ? MatchKnownLauncher(targetPath) : null;

            // A freshly launched process doesn't reliably get Windows' foreground focus on its
            // own - confirmed by real testing (a game needed a manual click after starting) -
            // actively find its window once it appears and force focus onto it, same mechanism
            // LaunchWebsite below already uses for the same reason.
            _ = Task.Run(() =>
            {
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    Thread.Sleep(100);
                    var newWindow = WindowEnumerator.EnumerateEligibleWindows().FirstOrDefault(h => !before.Contains(h));
                    if (!newWindow.IsNull)
                    {
                        WindowEnumerator.ForceForeground(newWindow);
                        LogFocusAttempt($"Found window after {attempt + 1} attempt(s), ForceForeground called for '{targetPath}'.");

                        if (launcherToCloseAfter is { } launcher)
                        {
                            WatchAndCloseLauncherAfter(newWindow, launcher);
                        }

                        return;
                    }
                }

                LogFocusAttempt($"Gave up after 5s - no new eligible window ever appeared for '{targetPath}'.");
            });

            return true;
        }
        catch (Exception ex)
        {
            try
            {
                File.AppendAllText(
                    Path.Combine(Path.GetTempPath(), "GridShell-TileLauncher-errors.log"),
                    $"{DateTime.Now:O} Launch failed for '{targetPath}' args='{tile.Arguments}': {ex}\n\n");
            }
            catch (IOException)
            {
            }

            return false;
        }
    }

    // Heroic's own exe carries its version in the filename ("Heroic-2.22.4-Portable-x64.exe" -
    // it's a standalone portable exe, not a real install with a fixed name), so it's matched by
    // prefix rather than the exact-filename equality every other launcher here uses.
    private static readonly (GameLauncher Launcher, string ExeName)[] KnownLaunchers =
    {
        (GameLauncher.Steam, "Steam.exe"),
        (GameLauncher.Heroic, "Heroic"),
        (GameLauncher.Itch, "itch.exe"),
    };

    private static GameLauncher? MatchKnownLauncher(string targetPath)
    {
        var fileName = Path.GetFileName(targetPath);
        foreach (var (launcher, exeName) in KnownLaunchers)
        {
            var matches = launcher == GameLauncher.Heroic
                ? fileName.StartsWith(exeName, StringComparison.OrdinalIgnoreCase)
                : string.Equals(fileName, exeName, StringComparison.OrdinalIgnoreCase);

            if (matches)
            {
                return launcher;
            }
        }

        return null;
    }

    /// <summary>Polls until gameWindow's owning process exits, then closes the launcher that
    /// started it - a hard Process.Kill() on whichever of its processes match the known exe
    /// name, not a graceful per-platform shutdown request (Steam has one via "steam://exit", the
    /// others don't, and a uniform approach is simpler and matches "close" literally).</summary>
    private static void WatchAndCloseLauncherAfter(HWND gameWindow, GameLauncher launcher)
    {
        GetWindowThreadProcessId(gameWindow, out var gamePid);
        if (gamePid == 0)
        {
            return;
        }

        try
        {
            using var gameProcess = Process.GetProcessById((int)gamePid);
            gameProcess.WaitForExit();
        }
        catch (ArgumentException)
        {
            // Already exited before we could even grab it - fall through and close the
            // launcher anyway, same as a normal exit.
        }

        var exePath = GameLauncherDetector.FindExe(launcher);
        if (exePath is null)
        {
            return;
        }

        var processName = Path.GetFileNameWithoutExtension(exePath);
        foreach (var process in Process.GetProcessesByName(processName))
        {
            try
            {
                process.Kill();
            }
            catch (Exception ex) when (ex is Win32Exception or InvalidOperationException)
            {
                // Already exited, or access denied (e.g. a differently-elevated instance) -
                // nothing more to do about it.
            }
        }
    }

    private static void LogFocusAttempt(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(Path.GetTempPath(), "GridShell-TileLauncher-errors.log"),
                $"{DateTime.Now:O} {message}\n\n");
        }
        catch (IOException)
        {
        }
    }

    /// <summary>A TargetPath of the form "appx:&lt;PackageFamilyName&gt;\&lt;relative exe
    /// path&gt;" resolves to that MSIX-packaged app's current install folder — the family name
    /// stays constant across updates, unlike the version-numbered folder itself, so a tile keeps
    /// working after the app updates instead of breaking. Returns null (caller falls back to the
    /// literal TargetPath) for anything else, or if the package can't be found. Public - shared
    /// with TaskbarWindowTracker, which needs the same resolution to match a running window's
    /// exe path back to the Tile that launched it.</summary>
    public static string? ResolveTargetPath(string path)
    {
        const string prefix = "appx:";
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var rest = path[prefix.Length..];
        var separatorIndex = rest.IndexOf('\\');
        if (separatorIndex < 0)
        {
            return null;
        }

        var familyName = rest[..separatorIndex];
        var relativeExePath = rest[(separatorIndex + 1)..];
        return AppxPackageResolver.ResolveExePath(familyName, relativeExePath);
    }

    private static bool LaunchWebsite(Tile tile)
    {
        if (WebsiteTileWindows.TryGetValue(tile.TargetPath, out var tracked) && IsWindow(tracked))
        {
            WindowEnumerator.ActivateMaximized(tracked);
            return true;
        }

        if (tile.BrowserExecutablePath is not { } browserExe)
        {
            return false;
        }

        try
        {
            var before = new HashSet<HWND>(WindowEnumerator.EnumerateEligibleWindows());
            Process.Start(new ProcessStartInfo(browserExe)
            {
                Arguments = $"--app={tile.TargetPath}",
                UseShellExecute = true,
                WindowStyle = ProcessWindowStyle.Maximized,
            });

            // Don't block the caller (a tile click) on this — whichever process ends up owning
            // the new window, track it once it appears so the NEXT launch of this same tile can
            // find and activate it instead of opening a duplicate. Also maximize it explicitly
            // here — Chromium can restore its own remembered window bounds for a site, which
            // would otherwise silently override the launch-maximized hint above.
            var url = tile.TargetPath;
            _ = Task.Run(() =>
            {
                for (var attempt = 0; attempt < 50; attempt++)
                {
                    Thread.Sleep(100);
                    var newWindow = WindowEnumerator.EnumerateEligibleWindows().FirstOrDefault(h => !before.Contains(h));
                    if (!newWindow.IsNull)
                    {
                        WebsiteTileWindows[url] = newWindow;
                        if (!IsZoomed(newWindow))
                        {
                            ShowWindow(newWindow, ShowWindowCommand.SW_MAXIMIZE);
                        }
                        WindowEnumerator.ForceForeground(newWindow);
                        return;
                    }
                }
            });

            return true;
        }
        catch (Exception ex) when (ex is Win32Exception or FileNotFoundException or InvalidOperationException)
        {
            return false;
        }
    }
}
