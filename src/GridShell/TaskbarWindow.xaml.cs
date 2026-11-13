using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using GridShell.Core.Interop;
using GridShell.Core.Taskbar;
using Vanara.PInvoke;
using static Vanara.PInvoke.Shell32;
using static Vanara.PInvoke.User32;

namespace GridShell;

public partial class TaskbarWindow : Window
{
    public const double SidebarWidth = 72;

    private readonly TaskbarWindowTracker _tracker = new();
    private readonly DispatcherTimer _clock;
    private readonly DispatcherTimer _battery;
    private readonly GridLauncher _launcher;
    private HWND _hwnd;

    // GetSystemMetrics/SHAppBarMessage/the work area can still be mid-transition for a brief
    // moment right when WM_DISPLAYCHANGE/WM_DPICHANGED first arrives — re-registering again
    // shortly after (once the OS has actually settled) catches anything the immediate call read
    // a stale/transitional value for (confirmed empirically: a value read right on the message
    // was sometimes wrong, the same read moments later was always correct).
    private readonly DispatcherTimer _displayChangeSettleTimer = new() { Interval = TimeSpan.FromMilliseconds(600) };

    // Set by PrepareForUpdateMessage, read by TaskbarWindow_Closed - lets the same Closed
    // handler pick the right exit code without the message handler racing Shutdown() directly.
    private bool _shuttingDownForUpdate;

    public System.Collections.ObjectModel.ObservableCollection<TaskbarWindowEntry> Windows => _tracker.Windows;

    public TaskbarWindow(GridLauncher launcher)
    {
        _launcher = launcher;

        InitializeComponent();
        DataContext = this;
        this.HideFromAltTab();

        // Placeholder sizing until SourceInitialized registers the real AppBar reservation
        // and positions the window to whatever rect the system actually grants it.
        Left = 0;
        Top = 0;
        Width = SidebarWidth;
        Height = SystemParameters.PrimaryScreenHeight;

        _clock = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        _clock.Tick += (_, _) => ClockText.Text = FormatClock();

        // Battery status barely changes faster than this — 15s (matching the clock) would just
        // be wasted Win32 calls.
        _battery = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
        _battery.Tick += (_, _) => UpdateBattery();

        _displayChangeSettleTimer.Tick += (_, _) =>
        {
            _displayChangeSettleTimer.Stop();
            RegisterAppBar();
        };

        SourceInitialized += TaskbarWindow_SourceInitialized;
        Loaded += TaskbarWindow_Loaded;
        Closed += TaskbarWindow_Closed;
    }

    private void TaskbarWindow_SourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;

        RegisterAppBar();
        PrintScreenHotkey.Register(_hwnd);

        HwndSource.FromHwnd(_hwnd.DangerousGetHandle())?.AddHook(WndProc);
    }

    private void RegisterAppBar()
    {
        var rect = AppBarManager.Register(_hwnd, ABE.ABE_LEFT, SidebarWidth);
        SetWindowPos(_hwnd, HWND.NULL, rect.left, rect.top, rect.Width, rect.Height,
            SetWindowPosFlags.SWP_NOZORDER | SetWindowPosFlags.SWP_NOACTIVATE);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (AltF4Blocker.IsSysCommandClose(msg, wParam))
        {
            handled = true;
        }
        else if (msg == SettingsChangedMessage.Id)
        {
            ((App)Application.Current).ApplyTheme();
            ((App)Application.Current).ReloadWallpaper();
            handled = true;
        }
        else if (msg == (int)WindowMessage.WM_DISPLAYCHANGE || msg == (int)WindowMessage.WM_DPICHANGED)
        {
            // A resolution OR DPI/scaling change invalidates the AppBar reservation (computed
            // in physical pixels at registration time) — re-register against the new screen
            // size, or the sidebar ends up wrongly sized/positioned. Immediately, for the common
            // case, and again shortly after in case the OS was still mid-transition the first time.
            RegisterAppBar();
            _displayChangeSettleTimer.Stop();
            _displayChangeSettleTimer.Start();
        }
        else if (msg == (int)WindowMessage.WM_HOTKEY && (int)wParam == PrintScreenHotkey.HotkeyId)
        {
            ScreenCaptureFlow.CaptureSnippet();
            handled = true;
        }
        else if (msg == PrepareForUpdateMessage.Id)
        {
            _shuttingDownForUpdate = true;
            handled = true;
        }

        return IntPtr.Zero;
    }

    private void TaskbarWindow_Loaded(object sender, RoutedEventArgs e)
    {
        _tracker.Start();
        ClockText.Text = FormatClock();
        _clock.Start();
        UpdateBattery();
        _battery.Start();
    }

    // "3:45 PM" doesn't fit a 56px-wide column without clipping — split it across three
    // short lines instead, and drop seconds (a tiny sidebar clock updating every second
    // adds noise without much value).
    private static string FormatClock() => DateTime.Now.ToString("h:mm\ntt\nddd");

    private BluetoothBatteryWindow? _bluetoothBatteryWindow;

    private void BatteryPanel_Click(object sender, MouseButtonEventArgs e)
    {
        if (_bluetoothBatteryWindow is { IsVisible: true })
        {
            return;
        }

        var topLeft = BatteryPanel.PointToScreen(new System.Windows.Point(0, 0));
        var bottom = topLeft.Y + BatteryPanel.ActualHeight;

        _bluetoothBatteryWindow = new BluetoothBatteryWindow(SidebarWidth + 8, bottom);
        _bluetoothBatteryWindow.Show();
    }

    private void ControllerHintButton_Click(object sender, RoutedEventArgs e)
    {
        var button = (Button)sender;
        var topLeft = button.PointToScreen(new System.Windows.Point(0, 0));
        var bottom = topLeft.Y + button.ActualHeight;

        new ControllerHintWindow(SidebarWidth + 8, bottom).Show();
    }

    private void UpdateBattery()
    {
        var status = BatteryStatus.Read();
        BatteryPanel.Visibility = status.HasBattery ? Visibility.Visible : Visibility.Collapsed;
        if (!status.HasBattery)
        {
            return;
        }

        BatteryIconText.Text = BatteryGlyph(status);
        BatteryPercentText.Text = $"{status.Percent}%";
    }

    private static string BatteryGlyph(BatteryStatus status)
    {
        if (status.IsCharging)
        {
            return ""; // battery_charging_full
        }

        return status.Percent switch
        {
            <= 5 => "", // battery_alert
            <= 16 => "", // battery_1_bar
            <= 33 => "", // battery_2_bar
            <= 50 => "", // battery_3_bar
            <= 66 => "", // battery_4_bar
            <= 83 => "", // battery_5_bar
            <= 99 => "", // battery_6_bar
            _ => "", // battery_full
        };
    }

    private void TaskbarWindow_Closed(object? sender, EventArgs e)
    {
        _clock.Stop();
        _battery.Stop();
        _displayChangeSettleTimer.Stop();
        _tracker.Dispose();
        AppBarManager.Unregister(_hwnd, ABE.ABE_LEFT);
        PrintScreenHotkey.Unregister(_hwnd);

        // ShutdownMode is OnExplicitShutdown, so closing this window alone wouldn't otherwise
        // exit the app — but this IS GridShell's only real window, so make it do so. The exit
        // code only matters if GridShell.Watchdog is running (i.e. GridShell is registered as
        // the Windows shell) — it tells the watchdog this was a deliberate exit, so it should
        // hand off to Explorer once instead of relaunching GridShell (or, mid-update, just stop
        // watching instead of either). Harmless when not running under the watchdog.
        Application.Current.Shutdown(_shuttingDownForUpdate ? ShellExitCodes.Updating : ShellExitCodes.RestoreExplorer);
    }

    private void LauncherButton_Click(object sender, RoutedEventArgs e)
    {
        _launcher.Toggle();
    }

    /// <summary>Swaps the launcher button between the default glyph icon, a different glyph
    /// the user picked from Settings' curated gallery, or a custom image file - called at
    /// startup and again whenever settings change. A missing/unreadable custom image falls
    /// back to the glyph rather than leaving the button blank.</summary>
    public void ApplyLauncherIcon(GridShell.Core.Settings.AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.StartButtonIconImagePath))
        {
            try
            {
                var bitmap = new System.Windows.Media.Imaging.BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(settings.StartButtonIconImagePath);
                bitmap.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bitmap.EndInit();
                bitmap.Freeze();

                LauncherIconImage.Source = bitmap;
                LauncherIconImage.Visibility = Visibility.Visible;
                LauncherIconGlyph.Visibility = Visibility.Collapsed;
                return;
            }
            catch (Exception ex) when (ex is IOException or NotSupportedException or UriFormatException)
            {
                // Falls through to the glyph below.
            }
        }

        if (int.TryParse(settings.StartButtonIconCodepoint, System.Globalization.NumberStyles.HexNumber,
                null, out var codepoint))
        {
            LauncherIconGlyph.Text = char.ConvertFromUtf32(codepoint);
        }

        LauncherIconGlyph.Visibility = Visibility.Visible;
        LauncherIconImage.Visibility = Visibility.Collapsed;
    }

    private void WindowButton_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not TaskbarWindowEntry entry)
        {
            return;
        }

        if (User32.GetForegroundWindow() == entry.Handle)
        {
            TaskbarWindowTracker.Minimize(entry);
        }
        else
        {
            TaskbarWindowTracker.Activate(entry);
            NotifyGridHide();
        }
    }

    /// <summary>If GridShell.Grid happens to be open when a program is selected from the
    /// taskbar's window list, close it — otherwise it would stay covering the screen, on top of
    /// the window the user just chose.</summary>
    private static void NotifyGridHide()
    {
        foreach (var process in System.Diagnostics.Process.GetProcessesByName("GridShell.Grid"))
        {
            foreach (var hwnd in ProcessWindowCloser.FindWindowsOf(process.Id))
            {
                PostMessage(hwnd, HideGridMessage.Id, IntPtr.Zero, IntPtr.Zero);
            }
        }
    }
}
