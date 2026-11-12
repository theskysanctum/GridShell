using System.Runtime.InteropServices;

namespace GridShell.Core.Interop.WiFi;

/// <summary>Raw P/Invoke layer over wlanapi.dll (the "Native Wifi API") - struct field order
/// confirmed against emoacht/ManagedNativeWifi (MIT-licensed, actively maintained wrapper around
/// this same API; https://github.com/emoacht/ManagedNativeWifi), not guessed from prose docs.
/// Exists because Windows.Devices.WiFi (WinRT) depends on COM activation infrastructure that
/// explorer.exe normally hosts, and fails once GridShell itself is running as the shell with no
/// explorer.exe present - wlanapi.dll is a plain Win32 DLL call with no such dependency.</summary>
internal static class WlanInterop
{
    private const string Lib = "wlanapi.dll";

    [DllImport(Lib)]
    public static extern int WlanOpenHandle(uint dwClientVersion, IntPtr pReserved, out uint pdwNegotiatedVersion, out IntPtr phClientHandle);

    [DllImport(Lib)]
    public static extern int WlanCloseHandle(IntPtr hClientHandle, IntPtr pReserved);

    [DllImport(Lib)]
    public static extern void WlanFreeMemory(IntPtr pMemory);

    [DllImport(Lib)]
    public static extern int WlanEnumInterfaces(IntPtr hClientHandle, IntPtr pReserved, out IntPtr ppInterfaceList);

    [DllImport(Lib)]
    public static extern int WlanScan(IntPtr hClientHandle, [MarshalAs(UnmanagedType.LPStruct)] Guid pInterfaceGuid,
        IntPtr pDot11Ssid, IntPtr pIeData, IntPtr pReserved);

    [DllImport(Lib)]
    public static extern int WlanGetAvailableNetworkList(IntPtr hClientHandle, [MarshalAs(UnmanagedType.LPStruct)] Guid pInterfaceGuid,
        uint dwFlags, IntPtr pReserved, out IntPtr ppAvailableNetworkList);

    [DllImport(Lib)]
    public static extern int WlanConnect(IntPtr hClientHandle, [MarshalAs(UnmanagedType.LPStruct)] Guid pInterfaceGuid,
        ref WLAN_CONNECTION_PARAMETERS pConnectionParameters, IntPtr pReserved);

    [DllImport(Lib, CharSet = CharSet.Unicode)]
    public static extern int WlanSetProfile(IntPtr hClientHandle, [MarshalAs(UnmanagedType.LPStruct)] Guid pInterfaceGuid,
        uint dwFlags, [MarshalAs(UnmanagedType.LPWStr)] string strProfileXml, [MarshalAs(UnmanagedType.LPWStr)] string? strAllUserProfileSecurity,
        [MarshalAs(UnmanagedType.Bool)] bool bOverwrite, IntPtr pReserved, out uint pdwReasonCode);

    [DllImport(Lib)]
    public static extern int WlanDisconnect(IntPtr hClientHandle, [MarshalAs(UnmanagedType.LPStruct)] Guid pInterfaceGuid, IntPtr pReserved);

    [DllImport(Lib)]
    public static extern int WlanGetProfileList(IntPtr hClientHandle, [MarshalAs(UnmanagedType.LPStruct)] Guid pInterfaceGuid,
        IntPtr pReserved, out IntPtr ppProfileList);

    [DllImport(Lib, CharSet = CharSet.Unicode)]
    public static extern int WlanDeleteProfile(IntPtr hClientHandle, [MarshalAs(UnmanagedType.LPStruct)] Guid pInterfaceGuid,
        [MarshalAs(UnmanagedType.LPWStr)] string strProfileName, IntPtr pReserved);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WLAN_PROFILE_INFO
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strProfileName;
        public uint dwFlags;
    }

    /// <summary>Same dwNumberOfItems/dwIndex-then-fixed-size-array layout as
    /// WLAN_AVAILABLE_NETWORK_LIST/WLAN_INTERFACE_INFO_LIST.</summary>
    public static List<string> ReadProfileNameList(IntPtr listPtr)
    {
        var result = new List<string>();
        var count = Marshal.ReadInt32(listPtr, 0);
        var itemSize = Marshal.SizeOf<WLAN_PROFILE_INFO>();
        var firstItemPtr = IntPtr.Add(listPtr, sizeof(uint) * 2);

        for (var i = 0; i < count; i++)
        {
            var itemPtr = IntPtr.Add(firstItemPtr, itemSize * i);
            result.Add(Marshal.PtrToStructure<WLAN_PROFILE_INFO>(itemPtr).strProfileName);
        }

        return result;
    }

    public const uint WlanAvailableNetworkConnected = 0x00000001;
    public const int Dot11BssTypeInfrastructure = 1;
    public const int WlanConnectionModeProfile = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct DOT11_SSID
    {
        public uint uSSIDLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 32)]
        public byte[] ucSSID;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WLAN_AVAILABLE_NETWORK
    {
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strProfileName;
        public DOT11_SSID dot11Ssid;
        public int dot11BssType;
        public uint uNumberOfBssids;
        public bool bNetworkConnectable;
        public uint wlanNotConnectableReason;
        public uint uNumberOfPhyTypes;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 8)]
        public uint[] dot11PhyTypes;
        public bool bMorePhyTypes;
        public uint wlanSignalQuality;
        public bool bSecurityEnabled;
        public int dot11DefaultAuthAlgorithm;
        public int dot11DefaultCipherAlgorithm;
        public uint dwFlags;
        public uint dwReserved;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct WLAN_CONNECTION_PARAMETERS
    {
        public int wlanConnectionMode;
        [MarshalAs(UnmanagedType.LPWStr)]
        public string? strProfile;
        public IntPtr pDot11Ssid;
        public IntPtr pDesiredBssidList;
        public int dot11BssType;
        public uint dwFlags;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WLAN_INTERFACE_INFO
    {
        public Guid InterfaceGuid;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
        public string strInterfaceDescription;
        public int isState;
    }

    /// <summary>The first wireless interface this machine has, or null if there isn't one
    /// (no Wi-Fi adapter present/enabled).</summary>
    public static Guid? GetFirstInterfaceGuid(IntPtr clientHandle)
    {
        if (WlanEnumInterfaces(clientHandle, IntPtr.Zero, out var listPtr) != 0)
        {
            return null;
        }

        try
        {
            var count = Marshal.ReadInt32(listPtr, 0);
            if (count == 0)
            {
                return null;
            }

            var itemSize = Marshal.SizeOf<WLAN_INTERFACE_INFO>();
            var firstItemPtr = IntPtr.Add(listPtr, sizeof(uint) * 2);
            var info = Marshal.PtrToStructure<WLAN_INTERFACE_INFO>(firstItemPtr);
            return info.InterfaceGuid;
        }
        finally
        {
            WlanFreeMemory(listPtr);
        }
    }

    /// <summary>Parses a WLAN_AVAILABLE_NETWORK_LIST buffer (dwNumberOfItems, dwIndex, then that
    /// many fixed-size WLAN_AVAILABLE_NETWORK entries back to back) - caller owns freeing the
    /// buffer via WlanFreeMemory.</summary>
    public static List<WLAN_AVAILABLE_NETWORK> ReadAvailableNetworkList(IntPtr listPtr)
    {
        var result = new List<WLAN_AVAILABLE_NETWORK>();
        var count = Marshal.ReadInt32(listPtr, 0);
        var itemSize = Marshal.SizeOf<WLAN_AVAILABLE_NETWORK>();
        var firstItemPtr = IntPtr.Add(listPtr, sizeof(uint) * 2);

        for (var i = 0; i < count; i++)
        {
            var itemPtr = IntPtr.Add(firstItemPtr, itemSize * i);
            result.Add(Marshal.PtrToStructure<WLAN_AVAILABLE_NETWORK>(itemPtr));
        }

        return result;
    }

    public static string SsidToString(DOT11_SSID ssid)
    {
        var length = (int)Math.Min(ssid.uSSIDLength, 32);
        return System.Text.Encoding.UTF8.GetString(ssid.ucSSID, 0, length);
    }
}
