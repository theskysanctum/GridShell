using System.Management;
using System.Threading;

namespace GridShell.Core.Interop;

/// <summary>Hides any window belonging to a process spawned (at any depth) by a winget install
/// or uninstall from GridShell's own taskbar for as long as that operation runs. winget.exe
/// itself already runs with no window of its own (WingetInstaller's own ProcessStartInfo), but
/// the actual vendor installer/uninstaller it invokes underneath - MSI's msiexec, an
/// InstallShield/NSIS/Inno uninstaller, anything - is under no such obligation, and not every
/// package's manifest gives winget a switch that keeps it fully silent. GridShell, as a shell
/// replacement, can't let one of those leak into its own taskbar even for a second - that's
/// the entire illusion - so rather than trying to special-case every installer technology's
/// process name, this tracks the whole descendant tree of the one process GridShell actually
/// launched and hides all of it, root cause of the window notwithstanding.</summary>
public static class InstallerProcessSuppressor
{
    private static readonly HashSet<int> SuppressedPids = new();
    private static readonly Lock Gate = new();

    public static bool IsSuppressed(int pid)
    {
        lock (Gate)
        {
            return SuppressedPids.Contains(pid);
        }
    }

    /// <summary>Starts tracking rootPid's descendant tree, polling for new children every
    /// 150ms (cheap enough at this scale, fast enough to catch a short-lived installer dialog
    /// before it can flash into view) until disposed, at which point every PID this added is
    /// removed again.</summary>
    public static IDisposable Suppress(int rootPid)
    {
        lock (Gate)
        {
            SuppressedPids.Add(rootPid);
        }

        var cts = new CancellationTokenSource();
        var pollTask = Task.Run(() => PollAsync(rootPid, cts.Token));
        return new Scope(cts, pollTask);
    }

    private static async Task PollAsync(int rootPid, CancellationToken token)
    {
        var owned = new List<int> { rootPid };

        // The cleanup below has to run no matter how this loop exits - including a WMI
        // failure mode narrower than ManagementException. Confirmed exactly this bug live:
        // an exception escaping the loop (anything the inner catch didn't name) skipped
        // cleanup entirely, permanently leaving rootPid (and whatever descendants had
        // already been found) in SuppressedPids - and since Windows recycles PIDs, a later,
        // completely unrelated process reusing that PID would silently inherit the
        // suppression forever, with no way to tell from the outside. A try/finally makes
        // cleanup unconditional instead of just hoping every failure mode gets named.
        try
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    using var searcher = new ManagementObjectSearcher(
                        $"SELECT ProcessId, ParentProcessId FROM Win32_Process WHERE ParentProcessId IN ({string.Join(",", owned)})");

                    foreach (ManagementBaseObject result in searcher.Get())
                    {
                        var childPid = Convert.ToInt32(result["ProcessId"]);
                        if (!owned.Contains(childPid))
                        {
                            owned.Add(childPid);
                            lock (Gate)
                            {
                                SuppressedPids.Add(childPid);
                            }
                        }
                    }
                }
                catch (Exception ex) when (ex is not OutOfMemoryException)
                {
                    // WMI unavailable/restricted/transiently failing - skip this tick, retry
                    // next poll rather than failing the install/uninstall itself over a
                    // taskbar cosmetic.
                }

                try
                {
                    await Task.Delay(150, token);
                }
                catch (TaskCanceledException)
                {
                }
            }
        }
        finally
        {
            lock (Gate)
            {
                foreach (var pid in owned)
                {
                    SuppressedPids.Remove(pid);
                }
            }
        }
    }

    private sealed class Scope(CancellationTokenSource cts, Task pollTask) : IDisposable
    {
        public void Dispose()
        {
            cts.Cancel();
            try
            {
                pollTask.Wait(TimeSpan.FromSeconds(1));
            }
            catch (AggregateException)
            {
            }

            cts.Dispose();
        }
    }
}
