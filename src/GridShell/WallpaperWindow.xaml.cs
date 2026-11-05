using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
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
    private static readonly string[] ImageExtensions = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
    private static readonly string[] VideoExtensions = { ".mp4", ".webm", ".mov", ".mkv", ".avi" };

    // A generated wrapper page must be navigated to as a real file:// page, not via
    // NavigateToString - Chromium's "URL safety check" rejects a <video>/<img> src pointing at
    // file:// from a page that isn't itself file://, which NavigateToString's page isn't.
    private static readonly string GeneratedWrapperPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GridShell", "WallpaperWebView2", "wallpaper.html");

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
        SourceInitialized += (_, _) =>
        {
            SendToBottom();
            ApplyScreenGeometryNative();
        };
        Loaded += async (_, _) => await ReloadAsync();
    }

    private void SendToBottom()
    {
        var hwnd = new HWND(new WindowInteropHelper(this).Handle);
        SetWindowPos(hwnd, HWND.HWND_BOTTOM, 0, 0, 0, 0,
            SetWindowPosFlags.SWP_NOMOVE | SetWindowPosFlags.SWP_NOSIZE | SetWindowPosFlags.SWP_NOACTIVATE);
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
        }

        var settings = AppSettingsStore.Load();
        Web.CoreWebView2!.IsMuted = settings.WallpaperMuted;

        var url = settings.WallpaperUrl;
        if (!string.IsNullOrWhiteSpace(url))
        {
            // Strip any query string before checking the extension - a remote URL like
            // ".../video.webm?token=abc" is still a video, not an unrecognized URL.
            var extension = Path.GetExtension(url.Split('?', '#')[0]);
            var navigableUrl = ToNavigableUrl(url);

            if (VideoExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                NavigateToWrapperPage(
                    $"<video src=\"{navigableUrl}\" autoplay loop style=\"width:100vw;height:100vh;object-fit:cover\"></video>");
                return;
            }

            if (ImageExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            {
                NavigateToWrapperPage($"<img src=\"{navigableUrl}\" style=\"width:100vw;height:100vh;object-fit:cover\" />");
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

        NavigateToWrapperPage($"<img src=\"{new Uri(wallpaperPath).AbsoluteUri}\" style=\"width:100vw;height:100vh;object-fit:cover\" />");
    }

    /// <summary>Writes a tiny wrapper page containing <paramref name="bodyHtml"/> to disk and
    /// navigates to it as a real file:// page - see GeneratedWrapperPath's own comment for why
    /// NavigateToString can't be used when the body references a local file:// resource.</summary>
    private void NavigateToWrapperPage(string bodyHtml)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(GeneratedWrapperPath)!);
        File.WriteAllText(GeneratedWrapperPath,
            $"<html><body style=\"margin:0;overflow:hidden;background:#101010\">{bodyHtml}</body></html>");
        Web.CoreWebView2!.Navigate(new Uri(GeneratedWrapperPath).AbsoluteUri);
    }

    private static bool LooksLikeRemoteUrl(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        || value.StartsWith("file://", StringComparison.OrdinalIgnoreCase);

    private static string ToNavigableUrl(string value) =>
        LooksLikeRemoteUrl(value) ? value : new Uri(Path.GetFullPath(value)).AbsoluteUri;
}
