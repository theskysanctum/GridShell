using System.Linq;
using System.Windows.Threading;
using Windows.Gaming.Input;

namespace GridShell.Core.Interop;

/// <summary>Drives the desktop mouse cursor from a controller's left stick (movement) and
/// triggers (R2 = left click, L2 = right click) — a general mouse replacement, not specific to
/// GridShell's own windows. Left, not right, so the right stick stays free for directional
/// navigation (ControllerInput's own default). Automatically does nothing while a Game-kind
/// tile's process has foreground focus, so it never fights a game for the same stick/
/// triggers.</summary>
public sealed class ControllerMouseInput : IDisposable
{
    private const double Deadzone = 0.15;
    private const double MaxPixelsPerTick = 18.0;
    private const double TriggerThreshold = 0.5;

    private readonly DispatcherTimer _timer;
    private readonly GameForegroundDetector _gameDetector = new();
    private bool _leftDown;
    private bool _rightDown;

    public ControllerMouseInput()
    {
        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
        _timer.Tick += (_, _) => Poll();
    }

    public void Start() => _timer.Start();

    public void Stop()
    {
        _timer.Stop();
        ReleaseHeldButtons();
    }

    private void Poll()
    {
        var gamepad = Gamepad.Gamepads.FirstOrDefault();
        if (gamepad is null || _gameDetector.IsGameForeground())
        {
            ReleaseHeldButtons();
            return;
        }

        var reading = gamepad.GetCurrentReading();

        var x = ApplyDeadzone(reading.LeftThumbstickX);
        var y = ApplyDeadzone(reading.LeftThumbstickY);
        if (x != 0 || y != 0)
        {
            // Screen Y grows downward; stick Y grows upward — invert to move the cursor the
            // way the stick is actually pushed.
            SyntheticMouse.Move((int)(x * MaxPixelsPerTick), (int)(-y * MaxPixelsPerTick));
        }

        SetButtonState(reading.RightTrigger > TriggerThreshold, ref _leftDown, SyntheticMouse.LeftDown, SyntheticMouse.LeftUp);
        SetButtonState(reading.LeftTrigger > TriggerThreshold, ref _rightDown, SyntheticMouse.RightDown, SyntheticMouse.RightUp);
    }

    private static void SetButtonState(bool wantDown, ref bool isDown, Action down, Action up)
    {
        if (wantDown == isDown)
        {
            return;
        }

        if (wantDown)
        {
            down();
        }
        else
        {
            up();
        }

        isDown = wantDown;
    }

    private static double ApplyDeadzone(double value) => Math.Abs(value) < Deadzone ? 0 : value;

    private void ReleaseHeldButtons()
    {
        if (_leftDown)
        {
            SyntheticMouse.LeftUp();
            _leftDown = false;
        }

        if (_rightDown)
        {
            SyntheticMouse.RightUp();
            _rightDown = false;
        }
    }

    public void Dispose() => Stop();
}
