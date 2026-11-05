using Windows.Devices.Radios;

namespace GridShell.Core.Interop.WiFi;

/// <summary>Turns the Wi-Fi radio on/off, independent of any specific network.</summary>
public static class WiFiRadioManager
{
    public static async Task<Radio?> GetRadioAsync()
    {
        var radios = await Radio.GetRadiosAsync();
        return radios.FirstOrDefault(r => r.Kind == RadioKind.WiFi);
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
