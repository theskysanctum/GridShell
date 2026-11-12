using System.Runtime.InteropServices;

namespace GridShell.Core.Interop.Bluetooth;

/// <summary>Raw P/Invoke layer over bthprops.cpl's classic (non-LE) Bluetooth device APIs -
/// struct/function signatures confirmed against Microsoft's own Learn pages for each one
/// (ns-bluetoothapis-bluetooth_device_info_struct, ns-bluetoothapis-bluetooth_device_search_params,
/// nf-bluetoothapis-bluetoothfindfirstdevice/-findnextdevice/-finddeviceclose/-removedevice/
/// -authenticatedevice), not guessed. Exists because Windows.Devices.Bluetooth (WinRT) depends
/// on COM activation infrastructure that explorer.exe normally hosts, and fails once GridShell
/// itself runs as the shell with no explorer.exe present. Classic Bluetooth only - this API
/// cannot see Bluetooth LE devices (confirmed on Microsoft's own BluetoothFindFirstDevice page);
/// that's a deliberate, accepted tradeoff for not depending on WinRT.</summary>
internal static class BluetoothInterop
{
    private const string Lib = "bthprops.cpl";
    public const int MaxNameSize = 248;
    public const int MaxPasskeySize = 16;

    [StructLayout(LayoutKind.Sequential)]
    public struct BLUETOOTH_DEVICE_INFO
    {
        public uint dwSize;
        public ulong Address;
        public uint ulClassofDevice;
        public bool fConnected;
        public bool fRemembered;
        public bool fAuthenticated;
        public SYSTEMTIME stLastSeen;
        public SYSTEMTIME stLastUsed;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MaxNameSize)]
        public string szName;

        public static BLUETOOTH_DEVICE_INFO Create() => new() { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_INFO>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SYSTEMTIME
    {
        public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
    {
        public uint dwSize;
        public bool fReturnAuthenticated;
        public bool fReturnRemembered;
        public bool fReturnUnknown;
        public bool fReturnConnected;
        public bool fIssueInquiry;
        public byte cTimeoutMultiplier;
        public IntPtr hRadio;

        public static BLUETOOTH_DEVICE_SEARCH_PARAMS Create() =>
            new() { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_DEVICE_SEARCH_PARAMS>() };
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLUETOOTH_FIND_RADIO_PARAMS
    {
        public uint dwSize;

        public static BLUETOOTH_FIND_RADIO_PARAMS Create() =>
            new() { dwSize = (uint)Marshal.SizeOf<BLUETOOTH_FIND_RADIO_PARAMS>() };
    }

    [DllImport(Lib, SetLastError = true)]
    public static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS pbtsp, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO pbtdi);

    [DllImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindDeviceClose(IntPtr hFind);

    [DllImport(Lib, SetLastError = true)]
    public static extern IntPtr BluetoothFindFirstRadio(ref BLUETOOTH_FIND_RADIO_PARAMS pbtfrp, out IntPtr phRadio);

    [DllImport(Lib, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool BluetoothFindRadioClose(IntPtr hFind);

    [DllImport(Lib, SetLastError = true)]
    public static extern uint BluetoothRemoveDevice(ref ulong pAddress);

    /// <summary>pszPasskey=null (as used here) shows Windows' own native pairing wizard dialog
    /// and blocks until the user completes/cancels it - no custom UI/callback marshaling needed
    /// on GridShell's side, which is the whole appeal of this simpler mode over
    /// BluetoothAuthenticateDeviceEx's PIN/passkey/numeric-comparison callback flow.</summary>
    [DllImport(Lib, SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern uint BluetoothAuthenticateDevice(IntPtr hwndParent, IntPtr hRadio, ref BLUETOOTH_DEVICE_INFO pbtbi, string? pszPasskey, uint ulPasskeyLength);

    public static string AddressToId(ulong address) => address.ToString("X12");

    public static ulong IdToAddress(string id) => Convert.ToUInt64(id, 16);
}
