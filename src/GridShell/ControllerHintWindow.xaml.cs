using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GridShell.Core.Interop;

namespace GridShell;

/// <summary>"?" button popup - lists every currently-connected controller and the system-wide
/// shortcuts ControllerMouseInput drives everywhere (not GridShell's own grid/OSK navigation,
/// which is already visible in those windows' own UI). Never takes focus and auto-dismisses
/// after a fixed duration - purely a glanceable reminder, not something to interact with.</summary>
public partial class ControllerHintWindow : Window
{
    private static readonly TimeSpan DismissAfter = TimeSpan.FromSeconds(5);

    public ControllerHintWindow(double left, double bottom)
    {
        InitializeComponent();
        this.NeverActivate();

        var devices = PadReader.ListDevices();
        StatusText.Text = devices.Count == 0
            ? "No controller connected"
            : devices.Count == 1 ? "Controller connected" : $"{devices.Count} controllers connected";
        DeviceNamesList.ItemsSource = devices.Select(d => d.Name).ToList();

        BindingsList.ItemsSource = new[]
        {
            new { Button = "Left Stick", Action = "Move cursor" },
            new { Button = "Right Stick", Action = "Scroll" },
            new { Button = "L2 / R2", Action = "Right-click / Left-click" },
            new { Button = "Touchpad", Action = "Trackpad (DualShock 4 / DualSense)" },
            new { Button = "L1 / R1", Action = "Back / Forward (Alt+Left / Alt+Right)" },
        };

        Loaded += (_, _) =>
        {
            // Clamped against the work area the same way BluetoothBatteryWindow is - this opens
            // from a button near the bottom of the sidebar, close enough to the screen edge that
            // an unclamped placement could push it past it.
            var workArea = SystemParameters.WorkArea;
            Left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - ActualWidth));
            Top = Math.Max(workArea.Top, Math.Min(bottom - ActualHeight, workArea.Bottom - ActualHeight));

            Opacity = 0;
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150)));
        };

        var dismissTimer = new DispatcherTimer { Interval = DismissAfter };
        dismissTimer.Tick += (_, _) =>
        {
            dismissTimer.Stop();
            var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
            fadeOut.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fadeOut);
        };
        dismissTimer.Start();
    }
}
