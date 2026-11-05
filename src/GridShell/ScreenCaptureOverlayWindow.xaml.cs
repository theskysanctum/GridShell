using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using GridShell.Core.Interop;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell;

/// <summary>A fullscreen, dimmed, click-through-free overlay for picking a screen region to
/// capture - drag to select, release to confirm, Escape to cancel. Spans the full virtual
/// screen in real physical pixels (same technique as WallpaperWindow), since the selected
/// region needs to map back to exact screen coordinates for Graphics.CopyFromScreen regardless
/// of which monitor or DPI it was drawn on.</summary>
public partial class ScreenCaptureOverlayWindow : Window
{
    private System.Windows.Point? _dragStart;

    /// <summary>The selected region in physical screen pixels, or null if the user cancelled
    /// (Escape) or never actually dragged out a selection. Set once, right before Close().</summary>
    public System.Drawing.Rectangle? SelectedRegion { get; private set; }

    private double _dpiScale = 1.0;
    private int _virtualScreenLeft;
    private int _virtualScreenTop;

    public ScreenCaptureOverlayWindow()
    {
        InitializeComponent();

        Left = SystemParameters.VirtualScreenLeft;
        Top = SystemParameters.VirtualScreenTop;
        Width = SystemParameters.VirtualScreenWidth;
        Height = SystemParameters.VirtualScreenHeight;

        SourceInitialized += (_, _) => ApplyScreenGeometryNative();
        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };
    }

    private void ApplyScreenGeometryNative()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        var bounds = ScreenCapture.FullVirtualScreenBounds();
        _virtualScreenLeft = bounds.Left;
        _virtualScreenTop = bounds.Top;
        _dpiScale = GetDpiForWindow(new HWND(hwnd)) / 96.0;

        SetWindowPos(new HWND(hwnd), HWND.HWND_TOPMOST, bounds.Left, bounds.Top, bounds.Width, bounds.Height,
            SetWindowPosFlags.SWP_SHOWWINDOW);
    }

    private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(this);
        HintText.Visibility = Visibility.Collapsed;
        SelectionRect.Visibility = Visibility.Visible;
        CaptureMouse();
    }

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (_dragStart is not { } start)
        {
            return;
        }

        var current = e.GetPosition(this);
        var left = Math.Min(start.X, current.X);
        var top = Math.Min(start.Y, current.Y);
        var width = Math.Abs(current.X - start.X);
        var height = Math.Abs(current.Y - start.Y);

        Canvas.SetLeft(SelectionRect, left);
        Canvas.SetTop(SelectionRect, top);
        SelectionRect.Width = width;
        SelectionRect.Height = height;
    }

    private void Window_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragStart is not { } start)
        {
            return;
        }

        ReleaseMouseCapture();
        var end = e.GetPosition(this);
        _dragStart = null;

        var left = Math.Min(start.X, end.X);
        var top = Math.Min(start.Y, end.Y);
        var width = Math.Abs(end.X - start.X);
        var height = Math.Abs(end.Y - start.Y);

        // A click with no real drag isn't a usable selection - treat it the same as Escape
        // rather than capturing a 0x0 (or 1x1) region.
        if (width < 2 || height < 2)
        {
            Close();
            return;
        }

        SelectedRegion = new System.Drawing.Rectangle(
            (int)(left * _dpiScale) + _virtualScreenLeft,
            (int)(top * _dpiScale) + _virtualScreenTop,
            (int)(width * _dpiScale),
            (int)(height * _dpiScale));

        Close();
    }

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            SelectedRegion = null;
            Close();
        }
    }
}
