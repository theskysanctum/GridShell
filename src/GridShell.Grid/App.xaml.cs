using System.Linq;
using System.Threading;
using System.Windows;
using GridShell.Core.Controllers;
using GridShell.Core.Interop;
using GridShell.Core.RealDebrid;
using GridShell.Core.Settings;
using static Vanara.PInvoke.User32;

namespace GridShell;

/// <summary>GridShell.Grid: the launcher grid, as its own long-lived process. It's pre-warmed
/// hidden by the always-running GridShell taskbar process right at startup (no "--show" arg),
/// then just shows/hides itself in place on each toggle — instant, since there's no process
/// startup cost left to pay by then. "--show" is only used for the rare cold-start fallback
/// (pre-warmed process missing/crashed), where the user is asking for it right now.</summary>
public partial class App : System.Windows.Application
{
    // GridLauncher's cold-start fallback can race the pre-warmed instance (started but not yet
    // showing a window within its retry window) and launch a genuine second process — this
    // guard makes that provably impossible rather than just less likely: only the first instance
    // ever gets as far as creating a window; every later one forwards its intent and exits.
    // Must be a field (not a local) so it isn't released by GC before the process exits.
    private static Mutex? _singleInstanceMutex;

    /// <summary>Listens for downloads the bundled Chrome extension forwards here instead of
    /// letting Chrome handle them itself - started once for this process's whole lifetime,
    /// independent of whether the Download Manager window is currently open.</summary>
    public DownloadBridgeServer DownloadBridge { get; } = new();

    /// <summary>The Download Manager's actual file-transfer engine - started once for this
    /// process's whole lifetime, same reasoning as DownloadBridge above.</summary>
    public Aria2Daemon Aria2 { get; } = new();

    private void Window_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is Window window)
        {
            window.FitToWorkArea();
        }
    }

    // A reused singleton (like MainWindow itself), not recreated per-toggle - downloads
    // should keep accumulating/running whether or not the window is currently visible, and
    // DownloadBridge needs one single place to route forwarded downloads to regardless of
    // whether the window has ever been opened yet.
    private DownloadManagerWindow? _downloadManagerWindow;

    public DownloadManagerWindow ShowDownloadManager()
    {
        // Closing this window just hides it (see DownloadManagerWindow's own Closing handler),
        // so it's never actually destroyed once created - this genuinely only ever runs once.
        _downloadManagerWindow ??= new DownloadManagerWindow();

        _downloadManagerWindow.Show();
        _downloadManagerWindow.Activate();
        return _downloadManagerWindow;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // A bad catalog.json (confirmed live: a hand-edit with a JSON syntax error) used to
        // propagate an uncaught JsonException straight out of StoreWindow_Loaded's async void
        // handler and take down this entire process, not just the Store window - WPF has no
        // crash recovery of its own for an unhandled exception on the dispatcher thread. Logs
        // and keeps the process alive instead; the window that triggered it is left in whatever
        // state it was in, same as any other caught error elsewhere in this app.
        DispatcherUnhandledException += (_, args) =>
        {
            UnhandledExceptionLogger.LogAndNotify(args.Exception);
            args.Handled = true;
        };

        _singleInstanceMutex = new Mutex(true, "GridShell.Grid.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            if (e.Args.Contains("--show"))
            {
                NotifyExistingInstanceToShow();
            }
            else if (GetExplorerPathArg(e.Args) is { } explorerPath)
            {
                NotifyExistingInstanceToOpenExplorer(explorerPath);
            }

            Shutdown();
            return;
        }

        ControllerAutoApply.Start();

        // Applied before the first window is created so it never renders with the wrong
        // theme even briefly. ThemeMode (WPF's own Fluent light/dark) and ThemePalette (our
        // own custom-colored surfaces) are two separate systems that both need setting.
        var settings = AppSettingsStore.Load();
#pragma warning disable WPF0001 // ThemeMode is experimental, but XAML already used it statically before this
        ThemeMode = settings.Theme == AppTheme.Light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
        ThemePalette.Apply(Resources, settings.Theme);
        var accent = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.AccentColor)!;
        ThemePalette.ApplyAccent(Resources, accent);

        DownloadBridge.DownloadReceived += request =>
            Dispatcher.Invoke(() => ShowDownloadManager().AddDownload(request.Url, request.Filename));
        DownloadBridge.Start();
        Aria2.Start();

        var window = new MainWindow();
        MainWindow = window;

        if (e.Args.Contains("--show"))
        {
            window.ShowGrid();
        }
        else
        {
            // A real Show() runs the window through its full Loaded pipeline — tile/icon
            // loading, first layout pass — paying that cost now instead of at the first real
            // toggle. Hide() right after, before the compositor presents a frame, so nothing
            // is actually visible on screen.
            window.Show();
            window.Hide();
        }

        // Rare path — only hit when GridShell.Grid wasn't already pre-warmed and running (the
        // usual case, handled above via NotifyExistingInstanceToOpenExplorer instead), e.g. a
        // manual/dev launch. Opens straight into the requested folder without also showing the
        // grid behind it.
        if (GetExplorerPathArg(e.Args) is { } coldStartExplorerPath)
        {
            new FileExplorerWindow(coldStartExplorerPath).Show();
        }
    }

    /// <summary>"--explorer &lt;path&gt;" - the launch mode the default-file-manager registry
    /// override (see DefaultFileManagerRegistration) uses, since that's what Windows invokes
    /// whenever anything asks to open a folder once it's enabled.</summary>
    private static string? GetExplorerPathArg(string[] args)
    {
        var index = Array.IndexOf(args, "--explorer");
        return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
    }

    private static void NotifyExistingInstanceToOpenExplorer(string path)
    {
        var ownPid = Environment.ProcessId;
        var otherPid = System.Diagnostics.Process.GetProcessesByName("GridShell.Grid")
            .FirstOrDefault(p => p.Id != ownPid)?.Id;

        if (otherPid is not { } pid)
        {
            return;
        }

        for (var attempt = 0; attempt < 10; attempt++)
        {
            var hwnds = ProcessWindowCloser.FindWindowsOf(pid);
            if (hwnds.Count > 0)
            {
                ExplorerHandoff.Write(path);
                foreach (var hwnd in hwnds)
                {
                    PostMessage(hwnd, OpenExplorerMessage.Id, IntPtr.Zero, IntPtr.Zero);
                }

                return;
            }

            Thread.Sleep(50);
        }
    }

    private static void NotifyExistingInstanceToShow()
    {
        var ownPid = Environment.ProcessId;
        var otherPid = System.Diagnostics.Process.GetProcessesByName("GridShell.Grid")
            .FirstOrDefault(p => p.Id != ownPid)?.Id;

        if (otherPid is not { } pid)
        {
            return;
        }

        // The other instance losing this same startup race against GridLauncher's own retry
        // window is exactly why we're here — it may not have a window yet either. Retry on the
        // same schedule FindGridWindowWithRetry uses, so a slow pre-warm doesn't silently drop
        // this "--show" request instead of just duplicating a process.
        for (var attempt = 0; attempt < 10; attempt++)
        {
            var hwnds = ProcessWindowCloser.FindWindowsOf(pid);
            if (hwnds.Count > 0)
            {
                foreach (var hwnd in hwnds)
                {
                    PostMessage(hwnd, ShowGridMessage.Id, IntPtr.Zero, IntPtr.Zero);
                }

                return;
            }

            Thread.Sleep(50);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Unlike DownloadBridge (just a loopback listener), aria2c is a real external process -
        // worth an explicit kill on a genuine app-initiated exit so it doesn't linger as an
        // orphan. Doesn't help against this app being killed outright (the dev workflow's
        // usual taskkill), only a real Shutdown() call.
        Aria2.Dispose();
        base.OnExit(e);
    }
}
