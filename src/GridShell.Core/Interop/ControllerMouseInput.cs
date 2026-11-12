using System.Linq;
using System.Windows.Threading;
using GridShell.Core.Controllers;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop;

/// <summary>Drives the desktop mouse cursor from a controller's left stick (movement),
/// triggers (R2 = left click, L2 = right click), a DualShock4/DualSense touchpad (relative
/// movement while a finger is down, same feel as a laptop trackpad), the right stick as a
/// scroll wheel whenever the on-screen keyboard isn't showing (so it doesn't fight
/// ControllerInput's own use of the right stick for OSK navigation while that IS showing), and
/// the shoulder buttons (L1/R1) as Alt+Left/Alt+Right (browser/file-explorer back and forward) —
/// a general mouse+shortcut replacement, not specific to GridShell's own windows. Automatically
/// does nothing while a Game-kind tile's process has foreground focus, so it never fights a game
/// for the same stick/triggers/touchpad/shoulders.</summary>
public sealed class ControllerMouseInput : IDisposable
{
    private const double Deadzone = 0.15;
    private const double MaxPixelsPerTick = 18.0;
    private const double TriggerThreshold = 0.5;
    private const double TouchpadPixelsPerUnit = 900.0;
    private const double ScrollUnitsPerTick = 6.0;
    private const double TapMaxMovement = 0.02;

    private static readonly TimeSpan PreferencesCacheDuration = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan TapMaxDuration = TimeSpan.FromMilliseconds(350);

    private readonly DispatcherTimer _timer;
    private readonly GameForegroundDetector _gameDetector = new();
    private readonly Dictionary<uint, (float X, float Y)> _lastTouchPosition = new();
    private readonly HashSet<uint> _touchpadClickDown = new();
    private readonly Dictionary<uint, (DateTime Started, float X, float Y)> _touchStart = new();
    private Dictionary<string, ControllerPreferences> _preferences = new();
    private DateTime _lastPreferencesLoad = DateTime.MinValue;
    private bool _leftDown;
    private bool _rightDown;
    private PadButtons _lastButtons;

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
        var maybeReading = PadReader.Read();
        if (maybeReading is not { } reading || _gameDetector.IsGameForeground())
        {
            ReleaseHeldButtons();
            _lastButtons = PadButtons.None;
            return;
        }

        var x = ApplyDeadzone(reading.LeftX);
        var y = ApplyDeadzone(reading.LeftY);
        if (x != 0 || y != 0)
        {
            // Screen Y grows downward; stick Y grows upward — invert to move the cursor the
            // way the stick is actually pushed.
            SyntheticMouse.Move((int)(x * MaxPixelsPerTick), (int)(-y * MaxPixelsPerTick));
        }

        SetButtonState(reading.RightTrigger > TriggerThreshold, ref _leftDown, SyntheticMouse.LeftDown, SyntheticMouse.LeftUp);
        SetButtonState(reading.LeftTrigger > TriggerThreshold, ref _rightDown, SyntheticMouse.RightDown, SyntheticMouse.RightUp);

        PollTouchpads();

        if (!OnScreenKeyboardState.IsVisible())
        {
            var scrollY = ApplyDeadzone(reading.RightY);
            if (scrollY != 0)
            {
                SyntheticMouse.Scroll((int)(scrollY * ScrollUnitsPerTick));
            }
        }

        // Edge-triggered (newly pressed this tick only) - held-down would otherwise fire a new
        // Alt+Left/Alt+Right every ~16ms for as long as the shoulder stays down.
        var pressedThisTick = reading.Buttons & ~_lastButtons;
        if (pressedThisTick.HasFlag(PadButtons.LeftShoulder))
        {
            SyntheticKeyboard.PressKeyWithModifiers(VK.VK_LEFT, new[] { VK.VK_MENU });
        }

        if (pressedThisTick.HasFlag(PadButtons.RightShoulder))
        {
            SyntheticKeyboard.PressKeyWithModifiers(VK.VK_RIGHT, new[] { VK.VK_MENU });
        }

        _lastButtons = reading.Buttons;
    }

    /// <summary>PadReader.Read()'s own merged reading has no touchpad data (it's DS4/DS5-
    /// specific, not a generic gamepad axis/button) - read it per-device instead.</summary>
    private void PollTouchpads()
    {
        RefreshPreferencesIfStale();

        var activeIds = new HashSet<uint>();
        foreach (var device in PadReader.ListDevices())
        {
            if (PadReader.TouchpadCount(device.Id) == 0)
            {
                continue;
            }

            activeIds.Add(device.Id);

            var prefs = _preferences.GetValueOrDefault(ControllerPreferencesStore.KeyFor(device.VendorId, device.ProductId));

            if (PadReader.ReadTouchpadFinger(device.Id, 0, 0) is not { } finger)
            {
                continue;
            }

            var clickMode = prefs?.TouchpadClickMode ?? TouchpadClickMode.Off;

            if (clickMode == TouchpadClickMode.PressAndHold)
            {
                if (finger.Down && _touchpadClickDown.Add(device.Id))
                {
                    SyntheticMouse.LeftDown();
                }
                else if (!finger.Down && _touchpadClickDown.Remove(device.Id))
                {
                    SyntheticMouse.LeftUp();
                }
            }
            else if (clickMode == TouchpadClickMode.Tap)
            {
                if (finger.Down && !_touchStart.ContainsKey(device.Id))
                {
                    _touchStart[device.Id] = (DateTime.UtcNow, finger.X, finger.Y);
                }
                else if (!finger.Down && _touchStart.TryGetValue(device.Id, out var start))
                {
                    _touchStart.Remove(device.Id);
                    var moved = Math.Max(Math.Abs(finger.X - start.X), Math.Abs(finger.Y - start.Y));
                    if (DateTime.UtcNow - start.Started <= TapMaxDuration && moved <= TapMaxMovement)
                    {
                        SyntheticMouse.LeftDown();
                        SyntheticMouse.LeftUp();
                    }
                }
            }
            else if (clickMode == TouchpadClickMode.PhysicalClick)
            {
                var pressed = PadReader.ReadDevice(device.Id)?.Buttons.HasFlag(PadButtons.TouchpadClick) == true;
                if (pressed && _touchpadClickDown.Add(device.Id))
                {
                    SyntheticMouse.LeftDown();
                }
                else if (!pressed && _touchpadClickDown.Remove(device.Id))
                {
                    SyntheticMouse.LeftUp();
                }
            }

            if (!finger.Down)
            {
                _lastTouchPosition.Remove(device.Id);
                continue;
            }

            if (_lastTouchPosition.TryGetValue(device.Id, out var last))
            {
                var sensitivity = prefs?.TouchpadSensitivity ?? 1.0;
                SyntheticMouse.Move(
                    (int)((finger.X - last.X) * TouchpadPixelsPerUnit * sensitivity),
                    (int)((finger.Y - last.Y) * TouchpadPixelsPerUnit * sensitivity));
            }

            _lastTouchPosition[device.Id] = (finger.X, finger.Y);
        }

        foreach (var staleId in _lastTouchPosition.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            _lastTouchPosition.Remove(staleId);
        }

        foreach (var staleId in _touchpadClickDown.Where(id => !activeIds.Contains(id)).ToList())
        {
            SyntheticMouse.LeftUp();
            _touchpadClickDown.Remove(staleId);
        }

        foreach (var staleId in _touchStart.Keys.Where(id => !activeIds.Contains(id)).ToList())
        {
            _touchStart.Remove(staleId);
        }
    }

    private void RefreshPreferencesIfStale()
    {
        var now = DateTime.UtcNow;
        if (now - _lastPreferencesLoad < PreferencesCacheDuration)
        {
            return;
        }

        _lastPreferencesLoad = now;
        _preferences = ControllerPreferencesStore.Load();
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

        foreach (var id in _touchpadClickDown.ToList())
        {
            SyntheticMouse.LeftUp();
            _touchpadClickDown.Remove(id);
        }

        _lastTouchPosition.Clear();
        _touchStart.Clear();
    }

    public void Dispose() => Stop();
}
