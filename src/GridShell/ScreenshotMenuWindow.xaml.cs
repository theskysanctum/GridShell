using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GridShell.Core.Interop;
using Vanara.PInvoke;

namespace GridShell;

/// <summary>The Window / Snippet / Fullscreen screenshot menu - navigable by D-pad/stick
/// (Up/Down), Accept (A) to confirm, Back (B)/Escape to cancel. Also works with mouse/keyboard
/// alone, for when there's no controller connected. Opened via the controller's Select+B - see
/// App.OpenScreenshotMenu. Plain PrintScreen alone bypasses this entirely and just takes an
/// immediate snippet.</summary>
public partial class ScreenshotMenuWindow : Window
{
    // Reads the live AccentBrush resource rather than hardcoding the default blue, so this
    // menu's selection highlight follows the user's chosen accent color like everything else.
    private static Brush SelectedBrush =>
        Application.Current.Resources["AccentBrush"] as Brush ?? Brushes.DodgerBlue;

    private readonly ControllerInput _controllerInput;
    private readonly HWND _previousForegroundWindow;
    private Border[] _items = Array.Empty<Border>();
    private int _selectedIndex;

    // Deactivating is itself part of Close()'s own sequence (confirming a selection closes
    // this window, which deactivates it, which without this guard would call Close() a second
    // time reentrantly from inside Window_Deactivated - WPF throws InvalidOperationException
    // for that, confirmed via the actual crash log, not just reasoned about.
    private bool _isClosing;

    /// <summary>previousForegroundWindow must be captured by the caller BEFORE constructing
    /// this window - by the time this window exists it has already stolen foreground focus for
    /// itself, so GetForegroundWindow() from in here would just return this window (or later,
    /// mid-close, an unpredictable transient value).</summary>
    public ScreenshotMenuWindow(ControllerInput controllerInput, HWND previousForegroundWindow)
    {
        _controllerInput = controllerInput;
        _previousForegroundWindow = previousForegroundWindow;

        InitializeComponent();

        _items = new[] { WindowItem, SnippetItem, FullscreenItem };
        UpdateSelection();

        _controllerInput.ActionTriggered += OnControllerAction;
        Closed += (_, _) => _controllerInput.ActionTriggered -= OnControllerAction;

        Loaded += (_, _) =>
        {
            Activate();
            Focus();
        };
    }

    private void OnControllerAction(ControllerAction action)
    {
        switch (action)
        {
            case ControllerAction.Up:
                Move(-1);
                break;
            case ControllerAction.Down:
                Move(1);
                break;
            case ControllerAction.Accept:
                Confirm(_selectedIndex);
                break;
            case ControllerAction.Back:
                CloseMenu();
                break;
        }
    }

    private void CloseMenu()
    {
        if (_isClosing)
        {
            return;
        }

        _isClosing = true;
        Close();
    }

    private void Move(int delta)
    {
        _selectedIndex = (_selectedIndex + delta + _items.Length) % _items.Length;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        for (var i = 0; i < _items.Length; i++)
        {
            _items[i].Background = i == _selectedIndex ? SelectedBrush : Brushes.Transparent;
        }
    }

    private void Confirm(int index)
    {
        CloseMenu();
        switch (index)
        {
            case 0:
                ScreenCaptureFlow.CaptureWindow(_previousForegroundWindow);
                break;
            case 1:
                ScreenCaptureFlow.CaptureSnippet();
                break;
            case 2:
                ScreenCaptureFlow.CaptureFullScreen();
                break;
        }
    }

    private void WindowItem_Click(object sender, MouseButtonEventArgs e) => Confirm(0);
    private void SnippetItem_Click(object sender, MouseButtonEventArgs e) => Confirm(1);
    private void FullscreenItem_Click(object sender, MouseButtonEventArgs e) => Confirm(2);

    private void Window_KeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Up:
                Move(-1);
                break;
            case Key.Down:
                Move(1);
                break;
            case Key.Enter:
                Confirm(_selectedIndex);
                break;
            case Key.Escape:
                CloseMenu();
                break;
        }
    }

    private void Window_Deactivated(object sender, EventArgs e) => CloseMenu();
}
