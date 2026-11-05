using Windows.Devices.WiFi;
using Windows.Networking.Connectivity;
using Windows.Security.Credentials;

namespace GridShell.Core.Interop.WiFi;

public sealed record WiFiNetworkInfo(string Ssid, int SignalBars, bool IsSecured, bool IsConnected);

public enum WiFiConnectResult
{
    Success,
    NeedsPassword,
    Failed,
}

/// <summary>Scans for and connects to Wi-Fi networks via the one WiFiAdapter this machine has.
/// Windows.Devices.WiFi works from this unpackaged desktop app without any manifest capability —
/// confirmed empirically (WiFiAdapter.RequestAccessAsync returns Allowed here).</summary>
public static class WiFiNetworkManager
{
    private static WiFiAdapter? _adapter;
    private static List<WiFiAvailableNetwork> _lastScan = new();

    private static async Task<WiFiAdapter?> GetAdapterAsync()
    {
        if (_adapter is not null)
        {
            return _adapter;
        }

        await WiFiAdapter.RequestAccessAsync();
        var adapters = await WiFiAdapter.FindAllAdaptersAsync();
        _adapter = adapters.FirstOrDefault();
        return _adapter;
    }

    public static async Task<List<WiFiNetworkInfo>> ScanAsync()
    {
        var adapter = await GetAdapterAsync();
        if (adapter is null)
        {
            return new List<WiFiNetworkInfo>();
        }

        await adapter.ScanAsync();
        _lastScan = adapter.NetworkReport.AvailableNetworks.ToList();

        // GetConnectionProfiles() lists every saved Wi-Fi profile, not just the active one —
        // GetInternetConnectionProfile() is the one that's actually providing connectivity now.
        var activeProfile = NetworkInformation.GetInternetConnectionProfile();
        var connectedSsid = activeProfile?.IsWlanConnectionProfile == true ? activeProfile.ProfileName : null;

        // The same AP often shows up once per band/BSSID — collapse to one entry per SSID,
        // keeping the strongest signal, the way Windows' own Wi-Fi list does.
        return _lastScan
            .Where(n => !string.IsNullOrEmpty(n.Ssid))
            .GroupBy(n => n.Ssid, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(n => n.SignalBars).First())
            .OrderByDescending(n => n.SignalBars)
            .Select(n => new WiFiNetworkInfo(
                n.Ssid,
                n.SignalBars,
                n.SecuritySettings.NetworkAuthenticationType != NetworkAuthenticationType.Open80211,
                n.Ssid == connectedSsid))
            .ToList();
    }

    public static async Task<WiFiConnectResult> ConnectAsync(string ssid, string? password = null)
    {
        var adapter = await GetAdapterAsync();
        var network = _lastScan
            .Where(n => n.Ssid == ssid)
            .OrderByDescending(n => n.SignalBars)
            .FirstOrDefault();

        if (adapter is null || network is null)
        {
            return WiFiConnectResult.Failed;
        }

        var result = string.IsNullOrEmpty(password)
            ? await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic)
            : await adapter.ConnectAsync(network, WiFiReconnectionKind.Automatic,
                new PasswordCredential { Password = password });

        return result.ConnectionStatus switch
        {
            WiFiConnectionStatus.Success => WiFiConnectResult.Success,
            WiFiConnectionStatus.InvalidCredential when string.IsNullOrEmpty(password) => WiFiConnectResult.NeedsPassword,
            _ => WiFiConnectResult.Failed,
        };
    }

    public static async Task DisconnectAsync(string ssid)
    {
        var adapter = await GetAdapterAsync();
        if (adapter is not null && _lastScan.Any(n => n.Ssid == ssid))
        {
            adapter.Disconnect();
        }
    }
}
