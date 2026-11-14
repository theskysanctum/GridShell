using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using GridShell.Core;
using GridShell.Core.Interop;
using GridShell.Core.Settings;
using Microsoft.Web.WebView2.Core;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell;

/// <summary>A borderless window spanning the full virtual screen, parked at the very bottom of
/// the z-order and never activatable, hosting a WebView2 that renders whatever "wallpaper" the
/// user wants. GridShell, as the registered shell, owns no desktop window of its own the way
/// Explorer does — without this, the background is just whatever was last drawn (typically
/// black). Defaults to the actual current Windows desktop wallpaper image so it's never blank,
/// but Settings can point it at a local image, video, .html file, or live URL instead.</summary>
public partial class WallpaperWindow : Window
{
    private static readonly string GeneratedWrapperPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GridShell", "WallpaperWebView2", "wallpaper.html");

    private readonly DispatcherTimer _displayChangeSettleTimer =
        new() { Interval = TimeSpan.FromMilliseconds(600) };

    public WallpaperWindow()
    {
        InitializeComponent();

        // Rough pre-HWND placeholder (WPF props, logical/DPI-scaled) - ApplyScreenGeometryNative
        // below takes over with the real, work-area-respecting bounds once the HWND exists.
        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        this.NeverActivate();
        _displayChangeSettleTimer.Tick += (_, _) =>
        {
            _displayChangeSettleTimer.Stop();
            ApplyScreenGeometryNative();
        };
        Closed += (_, _) => _displayChangeSettleTimer.Stop();

        SourceInitialized += (_, _) =>
        {
            SendToBottom();
            ApplyScreenGeometryNative();
            var hwnd = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
        };
        Loaded += async (_, _) => await ReloadAsync();

        // WS_EX_NOACTIVATE (via NeverActivate above) does NOT stop this window's WebView2 from
        // stealing keyboard focus on click and activating the window anyway - confirmed
        // empirically (WebView2 is a real windowed control; clicking it can activate its
        // hosting window regardless of that style - also a known, unfixed WebView2 platform
        // limitation, MicrosoftEdge/WebView2Feedback#1004). Activating a window promotes it
        // toward the top of the z-order, which for this fullscreen, HWND_BOTTOM-pinned window
        // means the entire wallpaper jumps in front of everything else (taskbar, every open
        // app) the instant it's clicked. Rather than fight WebView2's activation (not
        // controllable), just immediately undo the z-order fallout each time it happens.
        Activated += (_, _) => SendToBottom();
    }

    private void SendToBottom()
    {
        var hwnd = new HWND(new WindowInteropHelper(this).Handle);
        SetWindowPos(hwnd, HWND.HWND_BOTTOM, 0, 0, 0, 0,
            SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOACTIVATE);
    }

    /// <summary>Swallows Alt+F4/system-menu Close - this is infrastructure (the desktop
    /// background), never meant to be individually closable, and it can become the Win32-active
    /// window (see the Activated handler above), which would otherwise make Alt+F4 close it for
    /// real. Same AltF4Blocker helper TaskbarWindow/MainWindow already use unconditionally.</summary>
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (AltF4Blocker.IsSysCommandClose(msg, wParam))
        {
            handled = true;
        }
        else if (msg == (int)WindowMessage.WM_DISPLAYCHANGE || msg == (int)WindowMessage.WM_DPICHANGED)
        {
            ApplyScreenGeometryNative();
            _displayChangeSettleTimer.Stop();
            _displayChangeSettleTimer.Start();
        }

        return IntPtr.Zero;
    }

    /// <summary>Sizes to the same bounds MainWindow's own ApplyScreenGeometryNative uses - the
    /// primary screen's full height but starting at the AppBar-reserved work area's left edge,
    /// so the wallpaper occupies exactly the same space the Grid does, not the taskbar sidebar's
    /// own strip. Read directly from the OS in physical pixels for the same reason MainWindow
    /// does: SystemParameters.WorkArea is WPF's own cached, DPI-scaled copy, which can be stale.</summary>
    private void ApplyScreenGeometryNative()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero)
        {
            return;
        }

        var workArea = AppBarManager.GetWorkArea();
        var screenWidth = GetSystemMetrics(SystemMetric.SM_CXSCREEN);
        var screenHeight = GetSystemMetrics(SystemMetric.SM_CYSCREEN);

        SetWindowPos(new HWND(hwnd), HWND.NULL, workArea.left, 0, screenWidth - workArea.left, screenHeight,
            SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);
    }

    /// <summary>Re-reads AppSettings and re-navigates — called at startup and again whenever
    /// Settings posts SettingsChangedMessage, mirroring App.ApplyTheme's live-reload pattern.
    /// Deliberately doesn't set the HTML &lt;video&gt; "muted" attribute - that would mute the
    /// element itself regardless of WallpaperMuted, defeating the whole point of a mute toggle.
    /// Chromium's default autoplay policy blocks audible autoplay without either "muted" or a
    /// user gesture though, which a wallpaper never gets - EnsureCoreWebView2Async below passes
    /// --autoplay-policy=no-user-gesture-required so audible autoplay actually starts, leaving
    /// CoreWebView2.IsMuted as the one real mute switch.</summary>
    public async Task ReloadAsync()
    {
        if (Web.CoreWebView2 is null)
        {
            var options = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = "--autoplay-policy=no-user-gesture-required" };
            var userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GridShell", "WallpaperWebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: userDataFolder, options: options);
            await Web.EnsureCoreWebView2Async(environment);

            // It's a wallpaper, not a browser - a right-click context menu (Back/Reload/Save
            // video as/Inspect) would look like a bug, not a feature.
            Web.CoreWebView2!.Settings.AreDefaultContextMenusEnabled = false;
            Web.CoreWebView2.Settings.AreDevToolsEnabled = false;

            // A microphone-reactive Scene (see the Store's "Scenes" category) needs
            // getUserMedia({audio: true}) to work - WebView2's own default is to show a
            // permission prompt bar, which nothing could ever click on a window that's never
            // activatable and sits at the very bottom of the z-order. Auto-granted rather than
            // denied: this window only ever shows content the user themselves chose as their
            // wallpaper (a Scene, or a custom .html/live URL they set in Settings), never
            // arbitrary third-party content, and the mic is used locally for visualization only
            // - nothing here has any way to transmit audio anywhere.
            Web.CoreWebView2.PermissionRequested += (_, args) =>
            {
                if (args.PermissionKind == CoreWebView2PermissionKind.Microphone)
                {
                    args.State = CoreWebView2PermissionState.Allow;
                }
            };
        }

        var settings = AppSettingsStore.Load();
        Web.CoreWebView2!.IsMuted = settings.WallpaperMuted;

        var url = settings.WallpaperUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            var navigableUrl = MediaWrapperHtml.ToNavigableUrl(url);

            if (MediaWrapperHtml.IsVideo(url))
            {
                NavigateToWrapperPage(MediaWrapperHtml.BuildVideoHtml(navigableUrl, muted: false));
                return;
            }

            if (MediaWrapperHtml.IsImage(url))
            {
                NavigateToWrapperPage(MediaWrapperHtml.BuildImageHtml(navigableUrl));
                return;
            }

            Web.CoreWebView2.Navigate(navigableUrl);
            return;
        }

        var wallpaperPath = DesktopWallpaper.GetCurrentPath();
        if (wallpaperPath is null)
        {
            Web.CoreWebView2.NavigateToString("<html><body style=\"margin:0;background:#101010\"></body></html>");
            return;
        }

        NavigateToWrapperPage(MediaWrapperHtml.BuildImageHtml(new Uri(wallpaperPath).AbsoluteUri));
    }

    /// <summary>Writes a tiny wrapper page containing <paramref name="bodyHtml"/> to disk and
    /// navigates to it as a real file:// page - see GeneratedWrapperPath's own comment for why
    /// NavigateToString can't be used when the body references a local file:// resource.</summary>
    private void NavigateToWrapperPage(string bodyHtml)
    {
        var uri = MediaWrapperHtml.WriteAndGetUri(GeneratedWrapperPath, bodyHtml);
        Web.CoreWebView2!.Navigate(uri);
    }

    /// <summary>Pauses whatever &lt;video&gt; element the current wallpaper is showing - called
    /// while a game (opted in via Tile.PauseWallpaperWhileRunning) has foreground focus. A
    /// no-op when the current wallpaper is a static image (no &lt;video&gt; element to find).
    /// Explicit pause rather than hiding the window - the wallpaper already sits at
    /// HWND_BOTTOM, fully covered by a foreground game, so hiding it would have zero visual
    /// effect and wouldn't reliably stop GPU/CPU decode the way an explicit pause does.</summary>
    public async Task PauseAsync()
    {
        if (Web.CoreWebView2 is not null)
        {
            await Web.CoreWebView2.ExecuteScriptAsync("document.querySelector('video')?.pause();");
        }
    }

    public async Task ResumeAsync()
    {
        if (Web.CoreWebView2 is not null)
        {
            await Web.CoreWebView2.ExecuteScriptAsync("document.querySelector('video')?.play();");
        }
    }
}
