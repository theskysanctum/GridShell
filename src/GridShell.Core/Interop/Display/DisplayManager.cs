using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.User32;

namespace GridShell.Core.Interop.Display;

public sealed record MonitorInfo(
    string DeviceName,
    string FriendlyName,
    bool IsPrimary,
    int CurrentWidth,
    int CurrentHeight,
    int X,
    int Y,
    bool BrightnessSupported,
    uint Brightness);

public sealed record ResolutionOption(int Width, int Height, int Frequency)
{
    public override string ToString() => $"{Width} x {Height} @ {Frequency}Hz";
}

public static class DisplayManager
{
    public static List<MonitorInfo> GetMonitors()
    {
        var result = new List<MonitorInfo>();
        var brightnessByPosition = GetBrightnessByMonitorPosition();

        uint index = 0;
        while (true)
        {
            var device = new Gdi32.DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<Gdi32.DISPLAY_DEVICE>() };
            if (!EnumDisplayDevices(null, index, ref device, 0))
            {
                break;
            }

            index++;

            if (!device.StateFlags.HasFlag(Gdi32.DISPLAY_DEVICE_FLAGS.DISPLAY_DEVICE_ATTACHED_TO_DESKTOP))
            {
                continue;
            }

            var devMode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettingsEx(device.DeviceName, ENUM_CURRENT_SETTINGS, ref devMode, 0))
            {
                continue;
            }

            var x = devMode.dmPosition.X;
            var y = devMode.dmPosition.Y;
            var brightness = brightnessByPosition.GetValueOrDefault((x, y));

            result.Add(new MonitorInfo(
                device.DeviceName,
                GetMonitorFriendlyName(device.DeviceName)
                    ?? (string.IsNullOrWhiteSpace(device.DeviceString) ? device.DeviceName : device.DeviceString),
                device.StateFlags.HasFlag(Gdi32.DISPLAY_DEVICE_FLAGS.DISPLAY_DEVICE_PRIMARY_DEVICE),
                (int)devMode.dmPelsWidth,
                (int)devMode.dmPelsHeight,
                x,
                y,
                brightness is not null,
                brightness ?? 0));
        }

        return result;
    }

    /// <summary>EnumDisplayDevices with an adapter's own DeviceName as the first argument walks
    /// the monitor(s) attached to that adapter, whose DeviceString is the real monitor model
    /// (from EDID) — the adapter-level call above only gives the GPU's name.</summary>
    private static string? GetMonitorFriendlyName(string adapterDeviceName)
    {
        var monitor = new Gdi32.DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<Gdi32.DISPLAY_DEVICE>() };
        return EnumDisplayDevices(adapterDeviceName, 0, ref monitor, 0) && !string.IsNullOrWhiteSpace(monitor.DeviceString)
            ? monitor.DeviceString
            : null;
    }

    public static List<ResolutionOption> GetSupportedResolutions(string deviceName)
    {
        var seen = new HashSet<(int, int, int)>();
        var result = new List<ResolutionOption>();

        uint modeIndex = 0;
        while (true)
        {
            var devMode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettingsEx(deviceName, modeIndex, ref devMode, 0))
            {
                break;
            }

            modeIndex++;

            var key = ((int)devMode.dmPelsWidth, (int)devMode.dmPelsHeight, (int)devMode.dmDisplayFrequency);
            if (seen.Add(key))
            {
                result.Add(new ResolutionOption(key.Item1, key.Item2, key.Item3));
            }
        }

        return result
            .OrderByDescending(r => r.Width * r.Height)
            .ThenByDescending(r => r.Frequency)
            .ToList();
    }

    public static bool SetResolution(string deviceName, ResolutionOption resolution)
    {
        var devMode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
        if (!EnumDisplaySettingsEx(deviceName, ENUM_CURRENT_SETTINGS, ref devMode, 0))
        {
            return false;
        }

        devMode.dmPelsWidth = (uint)resolution.Width;
        devMode.dmPelsHeight = (uint)resolution.Height;
        devMode.dmDisplayFrequency = (uint)resolution.Frequency;
        devMode.dmFields = DMFIELDS.DM_PELSWIDTH | DMFIELDS.DM_PELSHEIGHT | DMFIELDS.DM_DISPLAYFREQUENCY;

        var result = ChangeDisplaySettingsEx(deviceName, in devMode, HWND.NULL,
            ChangeDisplaySettingsFlags.CDS_UPDATEREGISTRY, IntPtr.Zero);
        return (int)result == 0;
    }

    /// <summary>Makes the given display the primary (position (0,0)) by shifting every display's
    /// position so that one lands at the origin — Windows requires the primary to be there.</summary>
    public static bool SetPrimary(string deviceName)
    {
        var monitors = GetMonitors();
        var target = monitors.FirstOrDefault(m => m.DeviceName == deviceName);
        if (target is null)
        {
            return false;
        }

        var offsetX = -target.X;
        var offsetY = -target.Y;

        foreach (var monitor in monitors)
        {
            var devMode = new DEVMODE { dmSize = (ushort)Marshal.SizeOf<DEVMODE>() };
            if (!EnumDisplaySettingsEx(monitor.DeviceName, ENUM_CURRENT_SETTINGS, ref devMode, 0))
            {
                continue;
            }

            devMode.dmPosition = new POINT(devMode.dmPosition.X + offsetX, devMode.dmPosition.Y + offsetY);
            devMode.dmFields = DMFIELDS.DM_POSITION;

            ChangeDisplaySettingsEx(monitor.DeviceName, in devMode, HWND.NULL,
                ChangeDisplaySettingsFlags.CDS_UPDATEREGISTRY | ChangeDisplaySettingsFlags.CDS_NORESET, IntPtr.Zero);
        }

        return (int)ChangeDisplaySettingsEx(null, IntPtr.Zero, HWND.NULL, 0, IntPtr.Zero) == 0;
    }

    public static bool SetBrightness(string deviceName, uint value)
    {
        var monitor = GetMonitors().FirstOrDefault(m => m.DeviceName == deviceName);
        if (monitor is null)
        {
            return false;
        }

        foreach (var (hMonitor, physical) in EnumeratePhysicalMonitors())
        {
            using (physical)
            {
                var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(hMonitor, ref info) || info.rcMonitor.X != monitor.X || info.rcMonitor.Y != monitor.Y)
                {
                    continue;
                }

                return Dxva2.SetMonitorBrightness(physical.hPhysicalMonitor, value);
            }
        }

        return false;
    }

    private static Dictionary<(int X, int Y), uint?> GetBrightnessByMonitorPosition()
    {
        var result = new Dictionary<(int X, int Y), uint?>();

        foreach (var (hMonitor, physical) in EnumeratePhysicalMonitors())
        {
            using (physical)
            {
                var info = new MONITORINFO { cbSize = (uint)Marshal.SizeOf<MONITORINFO>() };
                if (!GetMonitorInfo(hMonitor, ref info))
                {
                    continue;
                }

                var key = (info.rcMonitor.X, info.rcMonitor.Y);
                var supported = Dxva2.GetMonitorBrightness(physical.hPhysicalMonitor, out _, out var current, out _);
                result[key] = supported ? current : null;
            }
        }

        return result;
    }

    private static IEnumerable<(HMONITOR hMonitor, Dxva2.DisposablePhysicalMonitor physical)> EnumeratePhysicalMonitors()
    {
        var handles = new List<HMONITOR>();
        EnumDisplayMonitors(HDC.NULL, default, (hMonitor, _, _, _) =>
        {
            handles.Add(hMonitor);
            return true;
        }, IntPtr.Zero);

        foreach (var hMonitor in handles)
        {
            if (!Dxva2.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor.DangerousGetHandle(), out var count) || count == 0)
            {
                continue;
            }

            var physicalMonitors = new Dxva2.PHYSICAL_MONITOR[count];
            if (!Dxva2.GetPhysicalMonitorsFromHMONITOR(hMonitor.DangerousGetHandle(), count, physicalMonitors))
            {
                continue;
            }

            // Only the first physical monitor per HMONITOR is used — a single logical display
            // backed by more than one physical monitor (a rare "clone" setup) isn't handled
            // specially, same scope as the rest of this feature.
            yield return (hMonitor, new Dxva2.DisposablePhysicalMonitor(physicalMonitors));
        }
    }
}
