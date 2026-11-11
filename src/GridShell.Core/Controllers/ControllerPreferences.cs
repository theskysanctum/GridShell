namespace GridShell.Core.Controllers;

/// <summary>How a touchpad press should drive a mouse click - PressAndHold mirrors the finger
/// (button down while touched, up on release, same as the trigger-click behavior), Tap fires a
/// full quick click as soon as a short touch ends (like a laptop trackpad's tap-to-click), so a
/// deliberate held press can still be used for drag-selection separately if desired. PhysicalClick
/// uses the touchpad's actual click button (DualShock4/DualSense only - a real button distinct
/// from touch, SDL_GAMEPAD_BUTTON_TOUCHPAD) instead of touch heuristics: down while held,
/// released when it's released, with no tap/drag ambiguity since it's a discrete button.</summary>
public enum TouchpadClickMode
{
    Off,
    PressAndHold,
    Tap,
    PhysicalClick,
}

/// <summary>Per-device controller preferences, keyed by "VID:PID" (hex) in ControllerPreferencesStore
/// - everything here defaults to "no override" so a device with no saved entry behaves exactly
/// like the original fixed 1:1 mapping VirtualPadBridge started with.</summary>
public sealed class ControllerPreferences
{
    /// <summary>Button remap: source PadButtons flag name -> target Xbox360Button name. Only
    /// entries present here override the identity mapping; everything else maps 1:1.</summary>
    public Dictionary<string, string> ButtonRemap { get; set; } = new();

    public byte? LedRed { get; set; }
    public byte? LedGreen { get; set; }
    public byte? LedBlue { get; set; }

    public TouchpadClickMode TouchpadClickMode { get; set; } = TouchpadClickMode.Off;

    /// <summary>Multiplier on top of ControllerMouseInput's base touchpad-to-mouse scale - 1.0
    /// is that base scale, not an absolute pixel value, so it stays meaningful if the base
    /// scale is ever retuned.</summary>
    public double TouchpadSensitivity { get; set; } = 1.0;

    public bool GyroEnabled { get; set; }
    public bool AccelEnabled { get; set; }

    public TriggerEffectMode RightTriggerMode { get; set; } = TriggerEffectMode.Off;
    public TriggerEffectMode LeftTriggerMode { get; set; } = TriggerEffectMode.Off;
}
