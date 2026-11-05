using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;

namespace GridShell.Core.Interop;

/// <summary>A snapshot of the system's battery state, via the same Win32 API the real Windows
/// taskbar's battery flyout reads from. HasBattery is false on a desktop (or a laptop reporting
/// no battery present), which the taskbar uses to hide the indicator entirely rather than show a
/// meaningless one.</summary>
public readonly record struct BatteryStatus(bool HasBattery, int Percent, bool IsCharging, bool IsOnAc)
{
    public static BatteryStatus Read()
    {
        if (!GetSystemPowerStatus(out var status))
        {
            return new BatteryStatus(false, 0, false, false);
        }

        var batteryFlag = (byte)status.BatteryFlag;

        // 128 = "no system battery"; 255 = "unknown" (also treated as absent — nothing
        // meaningful to show).
        if ((batteryFlag & 0x80) != 0 || batteryFlag == 0xFF || status.BatteryLifePercent == 255)
        {
            return new BatteryStatus(false, 0, false, false);
        }

        return new BatteryStatus(
            HasBattery: true,
            Percent: status.BatteryLifePercent,
            IsCharging: (batteryFlag & 0x08) != 0,
            IsOnAc: status.ACLineStatus == AC_STATUS.AC_ONLINE);
    }
}
