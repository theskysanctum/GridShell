using System.Runtime.InteropServices;

namespace GridShell.Core.Interop.Display;

/// <summary>Raw P/Invoke for the monitor DDC/CI brightness APIs (dxva2.dll) — no Vanara.PInvoke
/// package covers these, unlike the rest of this project's Win32 interop.</summary>
internal static class Dxva2
{
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    public struct PHYSICAL_MONITOR
    {
        public IntPtr hPhysicalMonitor;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string szPhysicalMonitorDescription;
    }

    [DllImport("dxva2.dll")]
    public static extern bool GetNumberOfPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, out uint pdwNumberOfPhysicalMonitors);

    [DllImport("dxva2.dll", CharSet = CharSet.Auto)]
    public static extern bool GetPhysicalMonitorsFromHMONITOR(IntPtr hMonitor, uint dwPhysicalMonitorArraySize, [Out] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll")]
    public static extern bool DestroyPhysicalMonitors(uint dwPhysicalMonitorArraySize, [In] PHYSICAL_MONITOR[] pPhysicalMonitorArray);

    [DllImport("dxva2.dll")]
    public static extern bool GetMonitorBrightness(IntPtr hMonitor, out uint minimumBrightness, out uint currentBrightness, out uint maximumBrightness);

    [DllImport("dxva2.dll")]
    public static extern bool SetMonitorBrightness(IntPtr hMonitor, uint newBrightness);

    /// <summary>Owns the one physical-monitor array GetPhysicalMonitorsFromHMONITOR allocated,
    /// releasing it via DestroyPhysicalMonitors on dispose.</summary>
    public readonly struct DisposablePhysicalMonitor : IDisposable
    {
        private readonly PHYSICAL_MONITOR[] _monitors;

        public DisposablePhysicalMonitor(PHYSICAL_MONITOR[] monitors) => _monitors = monitors;

        public IntPtr hPhysicalMonitor => _monitors[0].hPhysicalMonitor;

        public void Dispose() => DestroyPhysicalMonitors((uint)_monitors.Length, _monitors);
    }
}
