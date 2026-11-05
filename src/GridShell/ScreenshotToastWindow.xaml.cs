using System.Diagnostics;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GridShell.Core.Interop;

namespace GridShell;

/// <summary>GridShell's own lightweight "Screenshot has been taken" notification, replacing
/// Windows' own screenshot toast - top-right corner, auto-dismisses after a few seconds, click
/// to open the containing folder early. Never steals focus (NeverActivate), same as the
/// wallpaper/on-screen-keyboard windows, so it never interrupts whatever the user was doing.</summary>
public partial class ScreenshotToastWindow : Window
{
    private static readonly TimeSpan VisibleDuration = TimeSpan.FromSeconds(3);

    private readonly string? _filePath;
    private readonly DispatcherTimer _dismissTimer = new() { Interval = VisibleDuration };

    public ScreenshotToastWindow(string? filePath)
    {
        _filePath = filePath;

        InitializeComponent();
        this.NeverActivate();

        Loaded += (_, _) =>
        {
            Left = SystemParameters.WorkArea.Right - Width - 20;
            Top = SystemParameters.WorkArea.Top + 20;

            SlideTransform.X = 40;
            Opacity = 0;
            var slideIn = new DoubleAnimation(40, 0, TimeSpan.FromMilliseconds(200)) { EasingFunction = new CubicEase() };
            var fadeIn = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
            SlideTransform.BeginAnimation(TranslateTransform.XProperty, slideIn);
            BeginAnimation(OpacityProperty, fadeIn);
        };

        _dismissTimer.Tick += (_, _) =>
        {
            _dismissTimer.Stop();
            Dismiss();
        };
        _dismissTimer.Start();
    }

    private void Dismiss()
    {
        var fadeOut = new DoubleAnimation(Opacity, 0, TimeSpan.FromMilliseconds(200));
        fadeOut.Completed += (_, _) => Close();
        BeginAnimation(OpacityProperty, fadeOut);
    }

    private void Window_MouseLeftButtonUp(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        _dismissTimer.Stop();

        if (_filePath is not null)
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{_filePath}\"") { UseShellExecute = true });
        }

        Dismiss();
    }
}
