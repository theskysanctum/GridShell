using System.Linq;
using System.Windows.Threading;
using Windows.Gaming.Input;

namespace GridShell.Core.Interop;

public enum ControllerAction
{
    Up,
    Down,
    Left,
    Right,
    Accept,
    Back,
    TabPrevious,
    TabNext,
    ToggleMenu,
    OpenKeyboard,

    /// <summary>X/Square — currently only acted on by the on-screen keyboard, as a second way to
    /// backspace without navigating to the dedicated key.</summary>
    Backspace,

    /// <summary>Select/View + Start/Menu held together — an instant-kill shortcut for a stuck or
    /// unresponsive emulator, since Alt+F4 doesn't reach a fullscreen game and a controller is
    /// the only input device at hand. Only acted on while a recognized game/emulator has
    /// foreground focus; see App.xaml.cs.</summary>
    KillEmulator,

    /// <summary>Select/View + B held together — opens the controller-navigable screenshot menu
    /// (Window / Snippet / Fullscreen). Not Select+Home/Guide: that button isn't exposed to
    /// apps at all, Windows reserves it exclusively for the Xbox Game Bar overlay.</summary>
    ScreenshotMenu,

    /// <summary>Right stick click (R3) — currently only acted on by the on-screen keyboard, as
    /// a controller-reachable Shift/Caps Lock toggle (same one-shot/double-tap-for-lock
    /// behavior as tapping the on-screen Shift key).</summary>
    Shift,
}

/// <summary>Which stick (if any), in addition to the D-pad, drives Up/Down/Left/Right.</summary>
public enum NavigationStick
{
    /// <summary>The grid/menu default — the left stick drives the mouse cursor
    /// (ControllerMouseInput), so navigation uses the right one instead.</summary>
    None,
    Left,
    Right,
}

/// <summary>Polls Windows' Gamepad API (which transparently covers Xbox controllers and, per
/// Windows' own built-in HID translation, PlayStation DualSense/DualShock too — verified: a
/// connected DualSense shows up here as a standard named-button gamepad, not just as a raw HID
/// device) and raises high-level navigation actions. Directional actions auto-repeat while
/// held, like a keyboard; button actions fire once per press.</summary>
public sealed class ControllerInput : IDisposable
{
    private const double StickThreshold = 0.5;
    private static readonly TimeSpan InitialRepeatDelay = TimeSpan.FromMilliseconds(400);
    private static readonly TimeSpan RepeatInterval = TimeSpan.FromMilliseconds(120);

    private readonly DispatcherTimer _timer;
    private readonly Dictionary<ControllerAction, DateTime> _pressedSince = new();
    private readonly Dictionary<ControllerAction, DateTime> _nextRepeatAt = new();
    private readonly NavigationStick _stick;

    public event Action<ControllerAction>? ActionTriggered;

    public ControllerInput(NavigationStick stick = NavigationStick.Right)
    {
        _stick = stick;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        _pressedSince.Clear();
        _nextRepeatAt.Clear();
    }

    private void Poll()
    {
        var gamepad = Gamepad.Gamepads.FirstOrDefault();
        if (gamepad is null)
        {
            _pressedSince.Clear();
            _nextRepeatAt.Clear();
            return;
        }

        var reading = gamepad.GetCurrentReading();
        var buttons = reading.Buttons;

        var (stickX, stickY) = _stick switch
        {
            NavigationStick.Left => (reading.LeftThumbstickX, reading.LeftThumbstickY),
            NavigationStick.Right => (reading.RightThumbstickX, reading.RightThumbstickY),
            _ => (0.0, 0.0),
        };

        var active = new HashSet<ControllerAction>();
        if (buttons.HasFlag(GamepadButtons.DPadUp) || stickY > StickThreshold)
        {
            active.Add(ControllerAction.Up);
        }

        if (buttons.HasFlag(GamepadButtons.DPadDown) || stickY < -StickThreshold)
        {
            active.Add(ControllerAction.Down);
        }

        if (buttons.HasFlag(GamepadButtons.DPadLeft) || stickX < -StickThreshold)
        {
            active.Add(ControllerAction.Left);
        }

        if (buttons.HasFlag(GamepadButtons.DPadRight) || stickX > StickThreshold)
        {
            active.Add(ControllerAction.Right);
        }

        if (buttons.HasFlag(GamepadButtons.A))
        {
            active.Add(ControllerAction.Accept);
        }

        // Checked before plain B below, so holding both together fires only the screenshot
        // menu, not Back as well.
        if (buttons.HasFlag(GamepadButtons.View) && buttons.HasFlag(GamepadButtons.B))
        {
            active.Add(ControllerAction.ScreenshotMenu);
        }
        else if (buttons.HasFlag(GamepadButtons.B))
        {
            active.Add(ControllerAction.Back);
        }

        if (buttons.HasFlag(GamepadButtons.Y))
        {
            active.Add(ControllerAction.OpenKeyboard);
        }

        if (buttons.HasFlag(GamepadButtons.X))
        {
            active.Add(ControllerAction.Backspace);
        }

        if (buttons.HasFlag(GamepadButtons.RightThumbstick))
        {
            active.Add(ControllerAction.Shift);
        }

        if (buttons.HasFlag(GamepadButtons.LeftShoulder))
        {
            active.Add(ControllerAction.TabPrevious);
        }

        if (buttons.HasFlag(GamepadButtons.RightShoulder))
        {
            active.Add(ControllerAction.TabNext);
        }

        // Checked before plain Menu below, so holding both together fires only the kill
        // shortcut, not ToggleMenu as well.
        if (buttons.HasFlag(GamepadButtons.View) && buttons.HasFlag(GamepadButtons.Menu))
        {
            active.Add(ControllerAction.KillEmulator);
        }
        else if (buttons.HasFlag(GamepadButtons.Menu))
        {
            active.Add(ControllerAction.ToggleMenu);
        }

        var now = DateTime.UtcNow;

        foreach (var action in active)
        {
            if (!_pressedSince.ContainsKey(action))
            {
                _pressedSince[action] = now;
                _nextRepeatAt[action] = now + InitialRepeatDelay;
                ActionTriggered?.Invoke(action);
            }
            else if (IsRepeatable(action) && now >= _nextRepeatAt[action])
            {
                _nextRepeatAt[action] = now + RepeatInterval;
                ActionTriggered?.Invoke(action);
            }
        }

        foreach (var released in _pressedSince.Keys.Except(active).ToList())
        {
            _pressedSince.Remove(released);
            _nextRepeatAt.Remove(released);
        }
    }

    private static bool IsRepeatable(ControllerAction action) =>
        action is ControllerAction.Up or ControllerAction.Down or ControllerAction.Left or ControllerAction.Right
            or ControllerAction.Backspace;

    public void Dispose() => Stop();
}
