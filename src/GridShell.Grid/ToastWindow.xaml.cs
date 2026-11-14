using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using GridShell.Core.Interop;

namespace GridShell;

/// <summary>A small, generic "FYI" notification - top-right corner, auto-dismisses, never steals
/// focus. Same visual pattern as GridShell's own ScreenshotToastWindow (the taskbar process' copy
/// of this idea), just reusable with any message/duration since this project (GridShell.Grid)
/// needs one too and the two projects don't share UI code.</summary>
public partial class ToastWindow : Window
{
    /// <summary>isWarning swaps the fixed accent-colored info glyph for a themed warning one -
    /// still this same toast, not a MessageBox, for the "something failed" notifications that
    /// used to be plain native MessageBoxes throughout the app (Settings, File Explorer, Game
    /// Manager, Store) before they were all moved onto this.</summary>
    public ToastWindow(string message, TimeSpan duration, bool isWarning = false)
    {
        InitializeComponent();
        this.NeverActivate();
        MessageText.Text = message;

        if (isWarning)
        {
            IconText.Text = "";
            IconText.SetResourceReference(TextBlock.ForegroundProperty, "ErrorForegroundBrush");
        }

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

        var dismissTimer = new DispatcherTimer { Interval = duration };
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
