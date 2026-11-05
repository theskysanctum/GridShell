using System.Windows;
using GridShell.Core.Interop;
using GridShell.Core.Settings;
using static Vanara.PInvoke.User32;

namespace GridShell;

public partial class App : System.Windows.Application
{
    private TaskbarWindow? _taskbarWindow;
    private WallpaperWindow? _wallpaperWindow;
    private ScreenshotMenuWindow? _screenshotMenu;
    private readonly GridLauncher _launcher = new();
    private readonly GlobalKeyboardHook _keyboardHook = new();

    // The one-shot Hide() call at startup only catches Explorer's taskbar if it's already
    // running at that moment. When GridShell is the registered shell, Explorer isn't running
    // yet at startup — but something can still start it later (e.g. the user opening a File
    // Explorer window), and it then self-elects to show its own taskbar again since it doesn't
    // recognize GridShell's sidebar as an existing shell. Re-asserting on a timer, rather than
    // only reacting to one known trigger, catches that regardless of cause.
    private readonly System.Windows.Threading.DispatcherTimer _taskbarHideTimer =
        new() { Interval = TimeSpan.FromSeconds(2) };

    // Always running (like the keyboard hook), so the Menu/Start button can open the grid even
    // while it's hidden — mirrors the Win key's role. Grid navigation itself (arrows, A, B,
    // shoulder buttons) is handled separately, inside GridShell.Grid, only while visible.
    private readonly ControllerInput _controllerInput = new();

    // General mouse-cursor replacement (right stick + triggers) — also always running, since
    // it's meant to work on the desktop and other apps, not just GridShell's own windows. Stays
    // quiet on its own whenever a Game-kind tile's process has foreground focus.
    private readonly ControllerMouseInput _controllerMouse = new();

    // Same suppression rule as the mouse — Y/Triangle shouldn't steal a face button a game is
    // using for its own purposes.
    private readonly GameForegroundDetector _gameDetector = new();

    // The sidebar is registered as an always-on-top AppBar (so it stays above ordinary windows,
    // like a real taskbar) — which means it would otherwise sit drawn over a game/emulator that
    // genuinely fills the screen, instead of the game getting true fullscreen. Hidden for as
    // long as one has foreground focus, restored the moment it doesn't.
    private readonly System.Windows.Threading.DispatcherTimer _gameFullscreenTimer =
        new() { Interval = TimeSpan.FromMilliseconds(500) };
    private bool _sidebarHiddenForGame;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ApplyTheme();

        // GridShell's own sidebar fully replaces the real taskbar's role (window list,
        // launcher, clock), so the real one stays hidden for GridShell's whole lifetime —
        // not just while the grid overlay happens to be open.
        ShellTaskbarVisibility.Hide();
        _taskbarHideTimer.Tick += (_, _) => ShellTaskbarVisibility.Hide();
        _taskbarHideTimer.Start();

        _wallpaperWindow = new WallpaperWindow();
        _wallpaperWindow.Show();

        _taskbarWindow = new TaskbarWindow(_launcher);
        _taskbarWindow.Show();
        _taskbarWindow.ApplyLauncherIcon(AppSettingsStore.Load());
        MainWindow = _taskbarWindow;
        _launcher.WarmUp();

        _keyboardHook.WinKeyTapped += () => _launcher.Toggle();
        _keyboardHook.WinKeyHandlingEnabled = AppSettingsStore.Load().WinKeyHookEnabled;
        _keyboardHook.Start();

        _controllerInput.ActionTriggered += action =>
        {
            // The kill shortcut and the screenshot menu are the exceptions to the rule below —
            // the kill shortcut's entire purpose is to act WHILE a game has focus (a controller-
            // reachable stand-in for Alt+F4, which doesn't reach a fullscreen game, for one
            // that's stuck or unresponsive), and capturing gameplay is exactly when a
            // controller-driven screenshot menu is most useful.
            if (action == ControllerAction.KillEmulator)
            {
                KillForegroundGame();
                return;
            }

            if (action == ControllerAction.ScreenshotMenu)
            {
                if (_screenshotMenu is null)
                {
                    // Captured before the menu window exists - once it's shown/activated it
                    // would itself be the foreground window.
                    var previousForegroundWindow = GetForegroundWindow();
                    _screenshotMenu = new ScreenshotMenuWindow(_controllerInput, previousForegroundWindow);
                    _screenshotMenu.Closed += (_, _) => _screenshotMenu = null;
                    _screenshotMenu.Show();
                }
                return;
            }

            // Nothing else GridShell does with the controller should compete with a game/emulator
            // for its buttons — notably including ToggleMenu, since a user mapping controls in
            // an emulator's own input screen needs that press to reach the emulator raw, not get
            // eaten by GridShell opening the grid instead.
            if (_gameDetector.IsGameForeground())
            {
                return;
            }

            if (action == ControllerAction.ToggleMenu)
            {
                _launcher.Toggle();
            }
            else if (action == ControllerAction.OpenKeyboard)
            {
                NotifyGridOpenKeyboard();
            }
        };
        _controllerInput.Start();
        _controllerMouse.Start();

        _gameFullscreenTimer.Tick += (_, _) => UpdateSidebarVisibilityForGame();
        _gameFullscreenTimer.Start();
    }

    private void UpdateSidebarVisibilityForGame()
    {
        var shouldHide = _gameDetector.IsGameForeground();
        if (shouldHide == _sidebarHiddenForGame || _taskbarWindow is null)
        {
            return;
        }

        _sidebarHiddenForGame = shouldHide;
        if (shouldHide)
        {
            _taskbarWindow.Hide();
        }
        else
        {
            _taskbarWindow.Show();
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _taskbarHideTimer.Stop();
        _gameFullscreenTimer.Stop();
        _keyboardHook.Stop();
        _controllerInput.Dispose();
        _controllerMouse.Dispose();

        // The grid process is pre-warmed and long-lived now — it would otherwise outlive
        // the taskbar and keep running invisibly in the background. Centralized here (rather
        // than only in the Settings "Exit" flow) so it's covered no matter how the taskbar
        // itself comes to exit.
        _launcher.Shutdown();

        // Restore the real taskbar now that GridShell's own sidebar is going away.
        ShellTaskbarVisibility.Show();
        _wallpaperWindow?.Close();
        base.OnExit(e);
    }

    /// <summary>Re-reads AppSettings and re-navigates the wallpaper WebView2 — called at startup
    /// (implicitly, via WallpaperWindow's own constructor) and again whenever Settings posts
    /// SettingsChangedMessage, same live-reload pattern as ApplyTheme.</summary>
    public async void ReloadWallpaper()
    {
        if (_wallpaperWindow is not null)
        {
            await _wallpaperWindow.ReloadAsync();
        }
    }

    /// <summary>Force-kills whatever process currently has foreground focus — but only if it's a
    /// recognized game/emulator (same check the controller-suppression logic uses), so Select+
    /// Start can never kill an arbitrary unrelated app the user happens to be focused on.</summary>
    private void KillForegroundGame()
    {
        if (!_gameDetector.IsGameForeground())
        {
            return;
        }

        var hwnd = GetForegroundWindow();
        if (hwnd.IsNull)
        {
            return;
        }

        GetWindowThreadProcessId(hwnd, out var pid);

        try
        {
            using var process = System.Diagnostics.Process.GetProcessById((int)pid);
            process.Kill();
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
        }
    }

    private static void NotifyGridOpenKeyboard()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("GridShell.Grid"))
        {
            foreach (var hwnd in ProcessWindowCloser.FindWindowsOf(process.Id))
            {
                PostMessage(hwnd, OpenKeyboardMessage.Id, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }

    /// <summary>Re-reads AppSettings and re-applies the theme (and accent color) — called at
    /// startup and again whenever Settings (running in the separate GridShell.Grid process)
    /// posts SettingsChangedMessage to this process's window, so a change made there takes
    /// effect here immediately without needing to restart the taskbar.</summary>
    public void ApplyTheme()
    {
        var settings = AppSettingsStore.Load();
#pragma warning disable WPF0001 // ThemeMode is experimental, same suppression as GridShell.Grid
        ThemeMode = settings.Theme == AppTheme.Light ? ThemeMode.Light : ThemeMode.Dark;
#pragma warning restore WPF0001
        ThemePalette.Apply(Resources, settings.Theme);

        var color = (System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString(settings.AccentColor)!;
        Resources["AccentBrush"] = new System.Windows.Media.SolidColorBrush(color);

        // Same app-level DynamicResource pattern as AccentBrush - every window references
        // {DynamicResource AppFontFamily} instead of a hardcoded font.
        Resources["AppFontFamily"] = UiFontResolver.Resolve(settings);

        _keyboardHook.WinKeyHandlingEnabled = settings.WinKeyHookEnabled;

        _taskbarWindow?.ApplyLauncherIcon(settings);
    }
}
