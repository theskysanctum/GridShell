using System.Runtime.InteropServices;
using Vanara.PInvoke;
using static Vanara.PInvoke.Kernel32;

namespace GridShell.Core.Interop;

/// <summary>A CPU/RAM snapshot, for the System Stats desktop widget - same simple
/// readonly-record-struct-with-a-static-Read() shape as BatteryStatus, except CPU needs a
/// delta between two samples (kept as static state here) rather than a one-shot read.</summary>
public readonly record struct SystemStats(double CpuPercent, double RamPercent)
{
    private static ulong _lastIdle;
    private static ulong _lastTotal;
    private static bool _hasPreviousSample;

    public static SystemStats Read() => new(ReadCpuPercent(), ReadRamPercent());

    private static double ReadCpuPercent()
    {
        if (!GetSystemTimes(out var idleTime, out var kernelTime, out var userTime))
        {
            return 0;
        }

        var idle = ToTicks(idleTime);
        var total = ToTicks(kernelTime) + ToTicks(userTime);

        if (!_hasPreviousSample)
        {
            _lastIdle = idle;
            _lastTotal = total;
            _hasPreviousSample = true;
            return 0;
        }

        var idleDelta = idle - _lastIdle;
        var totalDelta = total - _lastTotal;
        _lastIdle = idle;
        _lastTotal = total;

        return totalDelta == 0 ? 0 : 100.0 * (1.0 - (double)idleDelta / totalDelta);
    }

    private static double ReadRamPercent()
    {
        var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref status) || status.ullTotalPhys == 0)
        {
            return 0;
        }

        return 100.0 * (status.ullTotalPhys - status.ullAvailPhys) / status.ullTotalPhys;
    }

    private static ulong ToTicks(System.Runtime.InteropServices.ComTypes.FILETIME ft) =>
        ((ulong)(uint)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;
}
