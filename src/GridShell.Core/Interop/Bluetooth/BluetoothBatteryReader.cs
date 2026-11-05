using Vanara.PInvoke;
using static Vanara.PInvoke.SetupAPI;

namespace GridShell.Core.Interop.Bluetooth;

public sealed record BluetoothDeviceBattery(string Name, int Percent);

/// <summary>Reads battery percentage for paired Bluetooth devices via the Bluetooth battery
/// device property (GUID {104EA319-6EE2-4701-BD47-8DDBF425BBE5}, property id 2) through
/// SetupAPI. This isn't a WinRT DeviceInformation property and isn't documented on
/// learn.microsoft.com - it's verified only against several independent open-source
/// implementations (toyg/BtBatteryTray, muedsa/BluetoothLEBatteryMonitor, and others that all
/// converge on the same GUID/PID), the same tier as other widely-relied-upon but
/// undocumented DEVPKEYs. Plain SetupAPI needs no app capability/MSIX packaging, unlike the
/// WinRT device broker.
///
/// Which PnP node actually carries the value varies by device: Windows populates it directly
/// on a BLE device's own node when the device exposes the standard GATT Battery Service, but
/// for classic Bluetooth headsets it often lives on a sibling "Hands-Free AG" (HFP) node
/// instead of the main audio device - so this scans every node under the Bluetooth device
/// class rather than assuming one specific node per device.</summary>
public static class BluetoothBatteryReader
{
    private static readonly DEVPROPKEY DevpkeyBluetoothBattery = new(
        0x104ea319, 0x6ee2, 0x4701, 0xbd, 0x47, 0x8d, 0xdb, 0xf4, 0x25, 0xbb, 0xe5, 2);

    public static List<BluetoothDeviceBattery> ReadAll()
    {
        var results = new List<BluetoothDeviceBattery>();

        using var deviceInfoSet = SetupDiGetClassDevs(GUID_DEVCLASS_BLUETOOTH, null, HWND.NULL, DIGCF.DIGCF_PRESENT);
        if (deviceInfoSet.IsInvalid)
        {
            return results;
        }

        foreach (var deviceInfoData in SetupDiEnumDeviceInfo(deviceInfoSet))
        {
            if (!SetupDiGetDeviceProperty(deviceInfoSet, deviceInfoData, DevpkeyBluetoothBattery, out var batteryValue)
                || batteryValue is not byte percent)
            {
                continue;
            }

            string name = "Unknown device";
            if (SetupDiGetDeviceProperty(deviceInfoSet, deviceInfoData, DEVPKEY_Device_FriendlyName, out var nameValue)
                && nameValue is string friendlyName && !string.IsNullOrWhiteSpace(friendlyName))
            {
                name = friendlyName;
            }

            results.Add(new BluetoothDeviceBattery(name, percent));
        }

        return results;
    }
}
