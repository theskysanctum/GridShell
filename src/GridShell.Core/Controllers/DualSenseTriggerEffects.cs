using GridShell.Core.Interop;

namespace GridShell.Core.Controllers;

public enum TriggerEffectMode
{
    Off = 0x00,
    Resistance = 0x01,
    Vibration = 0x06,
}

/// <summary>Builds and sends DualSense (PS5 controller) adaptive-trigger output reports via
/// PadReader.SendEffect - a raw passthrough SDL does not interpret itself (confirmed: SDL's own
/// PS5 driver, src/joystick/hidapi/SDL_hidapi_ps5.c, defines the DS5EffectsState_t struct this
/// mirrors but never populates its trigger-effect fields anywhere in SDL's own code).
///
/// EXPERIMENTAL / unofficial: Sony has not published this protocol. The struct layout and the
/// trigger-enable bits (0x04 right / 0x08 left in byte 0) come from the community project
/// pydualsense; the two modes implemented here (Resistance=0x01, Vibration=0x06) come from a
/// separate community reverse-engineering writeup (gist by stealth-alex) whose other,
/// stranger modes are marked "WIP"/unconfirmed even by that source and are deliberately not
/// implemented here. The two sources partially cross-confirm each other's mode numbering but
/// neither is an authoritative spec - expect this to need adjustment against real hardware
/// behavior, and treat it as best-effort, not a guaranteed-correct implementation.</summary>
public static class DualSenseTriggerEffects
{
    private const int ReportSize = 47;
    private const int RightTriggerOffset = 10;
    private const int LeftTriggerOffset = 21;

    public static void SetResistance(uint padId, bool rightTrigger, byte startPosition, byte force) =>
        Send(padId, rightTrigger, TriggerEffectMode.Resistance, startPosition, force, 0);

    public static void SetVibration(uint padId, bool rightTrigger, byte frequency, byte strength, byte startPosition) =>
        Send(padId, rightTrigger, TriggerEffectMode.Vibration, frequency, strength, startPosition);

    public static void Clear(uint padId, bool rightTrigger) =>
        Send(padId, rightTrigger, TriggerEffectMode.Off, 0, 0, 0);

    private static void Send(uint padId, bool rightTrigger, TriggerEffectMode mode, byte p0, byte p1, byte p2)
    {
        var data = new byte[ReportSize];
        data[0] = rightTrigger ? (byte)0x04 : (byte)0x08;

        var offset = rightTrigger ? RightTriggerOffset : LeftTriggerOffset;
        data[offset] = (byte)mode;
        data[offset + 1] = p0;
        data[offset + 2] = p1;
        data[offset + 3] = p2;

        PadReader.SendEffect(padId, data);
    }
}
