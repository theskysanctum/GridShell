using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace GridShell.Core.Interop.Bluetooth;

public sealed record PairedBluetoothDevice(string Id, string Name, bool IsConnected, bool IsLowEnergy);

public sealed record DiscoveredBluetoothDevice(string Id, string Name);

/// <summary>What the UI needs to show the user mid-pairing and how to respond. Mirrors
/// DevicePairingKinds/DevicePairingRequestedEventArgs, kept separate so the UI layer never
/// needs a direct WinRT dependency.</summary>
public sealed class PairingRequest
{
    public required DevicePairingKinds Kind { get; init; }
    public required string DisplayPin { get; init; }
    public required Action<bool, string?> Respond { get; init; }
}

public static class BluetoothDeviceManager
{
    public static async Task<List<PairedBluetoothDevice>> EnumeratePairedDevicesAsync()
    {
        var result = new List<PairedBluetoothDevice>();

        foreach (var info in await DeviceInformation.FindAllAsync(BluetoothDevice.GetDeviceSelectorFromPairingState(true)))
        {
            var device = await BluetoothDevice.FromIdAsync(info.Id);
            result.Add(new PairedBluetoothDevice(
                info.Id, info.Name, device?.ConnectionStatus == BluetoothConnectionStatus.Connected, IsLowEnergy: false));
        }

        foreach (var info in await DeviceInformation.FindAllAsync(BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)))
        {
            var device = await BluetoothLEDevice.FromIdAsync(info.Id);
            result.Add(new PairedBluetoothDevice(
                info.Id, info.Name, device?.ConnectionStatus == BluetoothConnectionStatus.Connected, IsLowEnergy: true));
        }

        return result;
    }

    public static async Task UnpairAsync(string deviceId)
    {
        var info = await DeviceInformation.CreateFromIdAsync(deviceId);
        if (info.Pairing.IsPaired)
        {
            await info.Pairing.UnpairAsync();
        }
    }

    /// <summary>Watches for nearby unpaired/discoverable devices until <see cref="StopDiscovery"/>
    /// is called. onFound may be raised multiple times for the same device as Windows learns
    /// more about it (e.g. its name arrives after its address).</summary>
    public static DeviceWatcher StartDiscovery(Action<DiscoveredBluetoothDevice> onFound)
    {
        var watcher = DeviceInformation.CreateWatcher(
            BluetoothDevice.GetDeviceSelectorFromPairingState(false),
            null,
            DeviceInformationKind.AssociationEndpoint);

        watcher.Added += (_, info) =>
        {
            if (!string.IsNullOrWhiteSpace(info.Name))
            {
                onFound(new DiscoveredBluetoothDevice(info.Id, info.Name));
            }
        };
        watcher.Updated += (_, update) => { };

        watcher.Start();
        return watcher;
    }

    public static void StopDiscovery(DeviceWatcher watcher)
    {
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted)
        {
            watcher.Stop();
        }
    }

    /// <summary>Pairs a device discovered via <see cref="StartDiscovery"/>. onPairingRequested is
    /// called whenever the device needs the user to confirm a PIN, enter one, or just confirm —
    /// call Respond on the given request once the user answers (via whatever UI shows it).</summary>
    public static async Task<bool> PairAsync(string deviceId, Func<PairingRequest, Task> onPairingRequested)
    {
        var info = await DeviceInformation.CreateFromIdAsync(deviceId);
        var custom = info.Pairing.Custom;

        custom.PairingRequested += async (_, args) =>
        {
            var tcs = new TaskCompletionSource();
            var request = new PairingRequest
            {
                Kind = args.PairingKind,
                DisplayPin = args.Pin ?? "",
                Respond = (accept, pin) =>
                {
                    // There's no explicit "reject" call on these args — declining just means
                    // not calling Accept at all, which times out the pairing attempt.
                    if (accept)
                    {
                        if (!string.IsNullOrEmpty(pin))
                        {
                            args.Accept(pin);
                        }
                        else
                        {
                            args.Accept();
                        }
                    }

                    tcs.TrySetResult();
                },
            };

            await onPairingRequested(request);
            await tcs.Task;
        };

        var result = await custom.PairAsync(
            DevicePairingKinds.ConfirmOnly | DevicePairingKinds.DisplayPin |
            DevicePairingKinds.ConfirmPinMatch | DevicePairingKinds.ProvidePin);

        return result.Status == DevicePairingResultStatus.Paired;
    }
}
