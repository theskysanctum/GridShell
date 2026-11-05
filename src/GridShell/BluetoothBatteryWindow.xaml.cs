using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using GridShell.Core.Interop.Bluetooth;

namespace GridShell;

public sealed class BluetoothBatteryDisplayItem
{
    public required string Name { get; init; }
    public required int Percent { get; init; }
    public string PercentDisplay => $"{Percent}%";
}

/// <summary>Click-the-battery popup - lists every connected Bluetooth device that reports a
/// battery level (not just the PC's own battery). Closes itself on any dismissal gesture
/// (click away, Escape, Enter) since it's purely informational, nothing to select.</summary>
public partial class BluetoothBatteryWindow : Window
{
    private bool _isClosing;

    public BluetoothBatteryWindow(double left, double bottom)
    {
        InitializeComponent();

        // Clamped against the work area rather than trusting the caller's coordinates
        // outright - the battery panel sits near the bottom of the sidebar, close enough to
        // the screen edge that an unclamped placement could push this past it.
        var workArea = SystemParameters.WorkArea;
        Left = Math.Max(workArea.Left, Math.Min(left, workArea.Right - Width));
        Top = Math.Max(workArea.Top, Math.Min(bottom - Height, workArea.Bottom - Height));

        Loaded += async (_, _) =>
        {
            Activate();
            Focus();
            var devices = await System.Threading.Tasks.Task.Run(BluetoothBatteryReader.ReadAll);

            var items = devices.ConvertAll(d => new BluetoothBatteryDisplayItem { Name = d.Name, Percent = d.Percent });
            DevicesList.ItemsSource = items;
            EmptyText.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        };
    }

    private void CloseWindow()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key is Key.Escape or Key.Enter)
        {
            CloseWindow();
        }
    }

    private void Window_Deactivated(object sender, EventArgs e) => CloseWindow();
}
