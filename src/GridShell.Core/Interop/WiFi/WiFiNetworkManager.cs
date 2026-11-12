using System.Runtime.InteropServices;
using System.Security;
using System.Text;

namespace GridShell.Core.Interop.WiFi;

public sealed record WiFiNetworkInfo(string Ssid, int SignalBars, bool IsSecured, bool IsConnected);

public enum WiFiConnectResult
{
    Success,
    NeedsPassword,
    Failed,
}

/// <summary>Scans for and connects to Wi-Fi networks via raw wlanapi.dll (WlanInterop) rather
/// than the WinRT Windows.Devices.WiFi API it replaced - that API depends on COM activation
/// infrastructure explorer.exe normally hosts and stopped working once GridShell itself runs
/// as the shell (no explorer.exe present). wlanapi.dll is a plain Win32 DLL call with no such
/// dependency.</summary>
public static class WiFiNetworkManager
{
    private static IntPtr _clientHandle = IntPtr.Zero;
    private static Guid? _interfaceGuid;
    private static List<WlanInterop.WLAN_AVAILABLE_NETWORK> _lastScan = new();

    private static bool EnsureHandle()
    {
        if (_clientHandle != IntPtr.Zero)
        {
            return _interfaceGuid is not null;
        }

        if (WlanInterop.WlanOpenHandle(2, IntPtr.Zero, out _, out _clientHandle) != 0)
        {
            _clientHandle = IntPtr.Zero;
            return false;
        }

        _interfaceGuid = WlanInterop.GetFirstInterfaceGuid(_clientHandle);
        return _interfaceGuid is not null;
    }

    public static async Task<List<WiFiNetworkInfo>> ScanAsync()
    {
        if (!EnsureHandle())
        {
            return new List<WiFiNetworkInfo>();
        }

        WlanInterop.WlanScan(_clientHandle, _interfaceGuid!.Value, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);

        // WlanScan is asynchronous (completion is normally signalled via WlanRegisterNotification) -
        // a short wait before reading results is simpler than wiring up that notification
        // callback, and matches how long a real scan typically takes.
        await Task.Delay(1500);

        if (WlanInterop.WlanGetAvailableNetworkList(_clientHandle, _interfaceGuid.Value, 0, IntPtr.Zero, out var listPtr) != 0)
        {
            return new List<WiFiNetworkInfo>();
        }

        try
        {
            _lastScan = WlanInterop.ReadAvailableNetworkList(listPtr);
        }
        finally
        {
            WlanInterop.WlanFreeMemory(listPtr);
        }

        // The same AP often shows up once per band/BSSID — collapse to one entry per SSID,
        // keeping the strongest signal, the way Windows' own Wi-Fi list does.
        return _lastScan
            .Select(n => (Ssid: WlanInterop.SsidToString(n.dot11Ssid), Network: n))
            .Where(n => !string.IsNullOrEmpty(n.Ssid))
            .GroupBy(n => n.Ssid, StringComparer.Ordinal)
            .Select(g => g.OrderByDescending(n => n.Network.wlanSignalQuality).First())
            .OrderByDescending(n => n.Network.wlanSignalQuality)
            .Select(n => new WiFiNetworkInfo(
                n.Ssid,
                // wlanSignalQuality is 0-100% — map to a 0-4 bar scale, same spirit as the
                // previous WinRT implementation's SignalBars.
                Math.Clamp((int)(n.Network.wlanSignalQuality / 25), 0, 4),
                n.Network.bSecurityEnabled,
                (n.Network.dwFlags & WlanInterop.WlanAvailableNetworkConnected) != 0))
            .ToList();
    }

    public static Task<WiFiConnectResult> ConnectAsync(string ssid, string? password = null)
    {
        if (!EnsureHandle())
        {
            return Task.FromResult(WiFiConnectResult.Failed);
        }

        var matches = _lastScan.Where(n => WlanInterop.SsidToString(n.dot11Ssid) == ssid).ToList();
        if (matches.Count == 0)
        {
            return Task.FromResult(WiFiConnectResult.Failed);
        }

        var network = matches.OrderByDescending(n => n.wlanSignalQuality).First();

        if (network.bSecurityEnabled && string.IsNullOrEmpty(password))
        {
            return Task.FromResult(WiFiConnectResult.NeedsPassword);
        }

        var profileXml = BuildProfileXml(ssid, network.bSecurityEnabled, password);
        var setResult = WlanInterop.WlanSetProfile(_clientHandle, _interfaceGuid!.Value, 0, profileXml, null, true, IntPtr.Zero, out _);
        if (setResult != 0)
        {
            return Task.FromResult(WiFiConnectResult.Failed);
        }

        var connectionParams = new WlanInterop.WLAN_CONNECTION_PARAMETERS
        {
            wlanConnectionMode = WlanInterop.WlanConnectionModeProfile,
            strProfile = ssid,
            pDot11Ssid = IntPtr.Zero,
            pDesiredBssidList = IntPtr.Zero,
            dot11BssType = WlanInterop.Dot11BssTypeInfrastructure,
            dwFlags = 0,
        };

        var connectResult = WlanInterop.WlanConnect(_clientHandle, _interfaceGuid.Value, ref connectionParams, IntPtr.Zero);
        return Task.FromResult(connectResult == 0 ? WiFiConnectResult.Success : WiFiConnectResult.Failed);
    }

    public static Task DisconnectAsync(string ssid)
    {
        if (EnsureHandle() && _lastScan.Any(n => WlanInterop.SsidToString(n.dot11Ssid) == ssid))
        {
            WlanInterop.WlanDisconnect(_clientHandle, _interfaceGuid!.Value, IntPtr.Zero);
        }

        return Task.CompletedTask;
    }

    /// <summary>Saved connection profiles ("remembered networks") - separate from ScanAsync's
    /// live, currently-in-range results, same distinction as paired vs discovered Bluetooth
    /// devices. A profile's name is always its SSID for profiles this app itself creates (see
    /// BuildProfileXml), but Windows' own Wi-Fi settings can save profiles under a different
    /// name, so this is deliberately keyed by profile name, not assumed to equal the SSID.</summary>
    public static Task<List<string>> ListSavedProfilesAsync()
    {
        if (!EnsureHandle())
        {
            return Task.FromResult(new List<string>());
        }

        if (WlanInterop.WlanGetProfileList(_clientHandle, _interfaceGuid!.Value, IntPtr.Zero, out var listPtr) != 0)
        {
            return Task.FromResult(new List<string>());
        }

        try
        {
            return Task.FromResult(WlanInterop.ReadProfileNameList(listPtr));
        }
        finally
        {
            WlanInterop.WlanFreeMemory(listPtr);
        }
    }

    public static Task ForgetProfileAsync(string profileName)
    {
        if (EnsureHandle())
        {
            WlanInterop.WlanDeleteProfile(_clientHandle, _interfaceGuid!.Value, profileName, IntPtr.Zero);
        }

        return Task.CompletedTask;
    }

    /// <summary>Connects using an already-saved profile directly - no password needed (or a
    /// BuildProfileXml/WlanSetProfile round-trip), since the credentials are already stored in
    /// the profile itself.</summary>
    public static Task<WiFiConnectResult> ConnectToSavedAsync(string profileName)
    {
        if (!EnsureHandle())
        {
            return Task.FromResult(WiFiConnectResult.Failed);
        }

        var connectionParams = new WlanInterop.WLAN_CONNECTION_PARAMETERS
        {
            wlanConnectionMode = WlanInterop.WlanConnectionModeProfile,
            strProfile = profileName,
            pDot11Ssid = IntPtr.Zero,
            pDesiredBssidList = IntPtr.Zero,
            dot11BssType = WlanInterop.Dot11BssTypeInfrastructure,
            dwFlags = 0,
        };

        var result = WlanInterop.WlanConnect(_clientHandle, _interfaceGuid!.Value, ref connectionParams, IntPtr.Zero);
        return Task.FromResult(result == 0 ? WiFiConnectResult.Success : WiFiConnectResult.Failed);
    }

    /// <summary>WLANProfile XML schema, confirmed against Microsoft's own WPA2-Personal profile
    /// sample (learn.microsoft.com/windows/win32/nativewifi/wpa2-personal-profile-sample) for
    /// the secured case; the open-network shape (authentication=open/encryption=none, no
    /// sharedKey) isn't from a verified Microsoft example, just the schema's documented optional
    /// elements - WlanSetProfile returning a non-zero result is the signal if that's wrong, not
    /// a silent failure.</summary>
    private static string BuildProfileXml(string ssid, bool secured, string? password)
    {
        var security = secured
            ? $"""
                      <authEncryption>
                          <authentication>WPA2PSK</authentication>
                          <encryption>AES</encryption>
                          <useOneX>false</useOneX>
                      </authEncryption>
                      <sharedKey>
                          <keyType>passPhrase</keyType>
                          <protected>false</protected>
                          <keyMaterial>{SecurityElement.Escape(password)}</keyMaterial>
                      </sharedKey>
              """
            : """
                      <authEncryption>
                          <authentication>open</authentication>
                          <encryption>none</encryption>
                          <useOneX>false</useOneX>
                      </authEncryption>
              """;

        var escapedSsid = SecurityElement.Escape(ssid);
        return $"""
            <?xml version="1.0"?>
            <WLANProfile xmlns="https://www.microsoft.com/networking/WLAN/profile/v1">
                <name>{escapedSsid}</name>
                <SSIDConfig>
                    <SSID>
                        <name>{escapedSsid}</name>
                    </SSID>
                </SSIDConfig>
                <connectionType>ESS</connectionType>
                <connectionMode>auto</connectionMode>
                <MSM>
                    <security>
            {security}
                    </security>
                </MSM>
            </WLANProfile>
            """;
    }
}
