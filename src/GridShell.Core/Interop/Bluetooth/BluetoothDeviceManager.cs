namespace GridShell.Core.Interop.Bluetooth;

public sealed record PairedBluetoothDevice(string Id, string Name, bool IsConnected, bool IsLowEnergy);

public sealed record DiscoveredBluetoothDevice(string Id, string Name);

/// <summary>Classic (non-LE) Bluetooth device enumeration/discovery/pairing/unpairing via raw
/// bthprops.cpl (BluetoothInterop) rather than the WinRT Windows.Devices.Bluetooth API it
/// replaced - that API depends on COM activation infrastructure explorer.exe normally hosts and
/// stopped working once GridShell itself runs as the shell. Bluetooth LE devices are no longer
/// enumerated at all (bthprops.cpl's device-find functions are classic-only, confirmed on
/// Microsoft's own docs) - a deliberate, accepted tradeoff, not an oversight.</summary>
public static class BluetoothDeviceManager
{
    public static Task<List<PairedBluetoothDevice>> EnumeratePairedDevicesAsync()
    {
        var result = new List<PairedBluetoothDevice>();
        var searchParams = BluetoothInterop.BLUETOOTH_DEVICE_SEARCH_PARAMS.Create();
        searchParams.fReturnRemembered = true;
        searchParams.fReturnConnected = true;
        searchParams.fReturnAuthenticated = true;

        var info = BluetoothInterop.BLUETOOTH_DEVICE_INFO.Create();
        var findHandle = BluetoothInterop.BluetoothFindFirstDevice(ref searchParams, ref info);
        if (findHandle == IntPtr.Zero)
        {
            return Task.FromResult(result);
        }

        try
        {
            do
            {
                result.Add(new PairedBluetoothDevice(
                    BluetoothInterop.AddressToId(info.Address), info.szName, info.fConnected, IsLowEnergy: false));
                info = BluetoothInterop.BLUETOOTH_DEVICE_INFO.Create();
            } while (BluetoothInterop.BluetoothFindNextDevice(findHandle, ref info));
        }
        finally
        {
            BluetoothInterop.BluetoothFindDeviceClose(findHandle);
        }

        return Task.FromResult(result);
    }

    public static Task UnpairAsync(string deviceId)
    {
        var address = BluetoothInterop.IdToAddress(deviceId);
        BluetoothInterop.BluetoothRemoveDevice(ref address);
        return Task.CompletedTask;
    }

    /// <summary>Polls for nearby unpaired/discoverable devices until <see cref="StopDiscovery"/>
    /// is called - unlike WinRT's push-based DeviceWatcher, bthprops.cpl's inquiry is a blocking
    /// call per poll, so this runs it on a background timer rather than registering a
    /// callback.</summary>
    public static System.Timers.Timer StartDiscovery(Action<DiscoveredBluetoothDevice> onFound)
    {
        var timer = new System.Timers.Timer(4000) { AutoReset = true };
        timer.Elapsed += (_, _) =>
        {
            var searchParams = BluetoothInterop.BLUETOOTH_DEVICE_SEARCH_PARAMS.Create();
            searchParams.fReturnUnknown = true;
            searchParams.fIssueInquiry = true;
            searchParams.cTimeoutMultiplier = 2; // ~2.56s inquiry window

            var info = BluetoothInterop.BLUETOOTH_DEVICE_INFO.Create();
            var findHandle = BluetoothInterop.BluetoothFindFirstDevice(ref searchParams, ref info);
            if (findHandle == IntPtr.Zero)
            {
                return;
            }

            try
            {
                do
                {
                    if (!string.IsNullOrWhiteSpace(info.szName))
                    {
                        onFound(new DiscoveredBluetoothDevice(BluetoothInterop.AddressToId(info.Address), info.szName));
                    }

                    info = BluetoothInterop.BLUETOOTH_DEVICE_INFO.Create();
                } while (BluetoothInterop.BluetoothFindNextDevice(findHandle, ref info));
            }
            finally
            {
                BluetoothInterop.BluetoothFindDeviceClose(findHandle);
            }
        };

        timer.Start();
        return timer;
    }

    public static void StopDiscovery(System.Timers.Timer timer) => timer.Stop();

    /// <summary>Pairs a device discovered via <see cref="StartDiscovery"/>. Unlike the previous
    /// WinRT implementation, this needs no custom pairing UI/callback - passing a null passkey
    /// to BluetoothAuthenticateDevice shows Windows' own native pairing wizard dialog and blocks
    /// until the user completes or cancels it there.</summary>
    public static Task<bool> PairAsync(string deviceId) => Task.Run(() =>
    {
        var address = BluetoothInterop.IdToAddress(deviceId);
        var info = BluetoothInterop.BLUETOOTH_DEVICE_INFO.Create();
        info.Address = address;

        // Blocking (Windows' own wizard dialog stays up until the user finishes/cancels it) -
        // run off the calling thread so it doesn't freeze the UI while that's showing.
        var result = BluetoothInterop.BluetoothAuthenticateDevice(IntPtr.Zero, IntPtr.Zero, ref info, null, 0);
        return result == 0;
    });
}
