using Windows.Devices.Radios;

namespace GridShell.Core.Interop.Bluetooth;

/// <summary>Turns the Bluetooth radio on/off, independent of any specific device.</summary>
public static class BluetoothRadioManager
{
    public static async Task<Radio?> GetRadioAsync()
    {
        var radios = await Radio.GetRadiosAsync();
        return radios.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
    }

    public static async Task<bool> IsOnAsync()
    {
        var radio = await GetRadioAsync();
        return radio?.State == RadioState.On;
    }

    public static async Task SetOnAsync(bool on)
    {
        var radio = await GetRadioAsync();
        if (radio is not null)
        {
            await radio.SetStateAsync(on ? RadioState.On : RadioState.Off);
        }
    }
}
