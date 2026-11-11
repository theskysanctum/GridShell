using GridShell.Core.Interop;

namespace GridShell.Core.Controllers;

/// <summary>Re-applies a device's saved LED color and adaptive trigger modes the moment it's
/// (re)connected - a controller's LED/trigger state is set by the console itself, not
/// remembered across a USB replug or app restart, so without this "sticks to the controller"
/// would only ever mean "for this session, until you unplug it." Each process that uses
/// PadReader has its own independent SDL state (see PadReader.DeviceConnected), so Start() must
/// be called once from each process that wants this (GridShell and GridShell.Grid both do).</summary>
public static class ControllerAutoApply
{
    private static bool _started;

    public static void Start()
    {
        if (_started)
        {
            return;
        }

        _started = true;
        PadReader.DeviceConnected += OnDeviceConnected;
    }

    private static void OnDeviceConnected(PadDevice device)
    {
        var prefs = ControllerPreferencesStore.Load()
            .GetValueOrDefault(ControllerPreferencesStore.KeyFor(device.VendorId, device.ProductId));

        if (prefs is null)
        {
            return;
        }

        if (prefs is { LedRed: { } r, LedGreen: { } g, LedBlue: { } b })
        {
            PadReader.SetLed(device.Id, r, g, b);
        }

        ApplyTriggerMode(device.Id, rightTrigger: true, prefs.RightTriggerMode);
        ApplyTriggerMode(device.Id, rightTrigger: false, prefs.LeftTriggerMode);
    }

    public static void ApplyTriggerMode(uint padId, bool rightTrigger, TriggerEffectMode mode)
    {
        switch (mode)
        {
            case TriggerEffectMode.Off:
                DualSenseTriggerEffects.Clear(padId, rightTrigger);
                break;
            case TriggerEffectMode.Resistance:
                DualSenseTriggerEffects.SetResistance(padId, rightTrigger, startPosition: 60, force: 150);
                break;
            case TriggerEffectMode.Vibration:
                DualSenseTriggerEffects.SetVibration(padId, rightTrigger, frequency: 180, strength: 40, startPosition: 60);
                break;
        }
    }
}
