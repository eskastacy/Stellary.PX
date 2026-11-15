using System.Runtime.InteropServices;

namespace Stellary.PX.Infrastructure;

public static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    private struct FILETIME
    {
        public uint Low;
        public uint High;
        public ulong ToUInt64() => ((ulong)High << 32) | Low;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("ntdll.dll")]
    public static extern int NtSetTimerResolution(uint desiredResolution, bool setResolution, out uint currentResolution);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryTimerResolution(out uint minResolution, out uint maxResolution, out uint currentResolution);

    public static (double cpuPercent, double ramPercent, double ramUsedGb, double ramTotalGb) SampleSystem()
    {
        var cpu = 0.0;
        if (GetSystemTimes(out var idle, out var kernel, out var user))
        {
            var idleTicks = idle.ToUInt64();
            var totalTicks = kernel.ToUInt64() + user.ToUInt64();
            lock (CpuGate)
            {
                if (_lastTotal != 0 && totalTicks > _lastTotal)
                {
                    var totalDelta = totalTicks - _lastTotal;
                    var idleDelta = idleTicks - _lastIdle;
                    cpu = Math.Clamp((1.0 - (double)idleDelta / totalDelta) * 100.0, 0, 100);
                }
                _lastTotal = totalTicks;
                _lastIdle = idleTicks;
            }
        }

        var ramPercent = 0.0;
        var usedGb = 0.0;
        var totalGb = 0.0;
        var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
        {
            ramPercent = (double)(mem.ullTotalPhys - mem.ullAvailPhys) / mem.ullTotalPhys * 100.0;
            usedGb = (mem.ullTotalPhys - mem.ullAvailPhys) / 1024.0 / 1024.0 / 1024.0;
            totalGb = mem.ullTotalPhys / 1024.0 / 1024.0 / 1024.0;
        }

        return (cpu, ramPercent, usedGb, totalGb);
    }

    private static readonly object CpuGate = new();
    private static ulong _lastTotal;
    private static ulong _lastIdle;

    public static void EnableRoundedCorners(IntPtr hwnd, bool enabled)
    {
        var value = enabled ? 2 : 1;
        try { DwmSetWindowAttribute(hwnd, 33, ref value, sizeof(int)); } catch { }
    }

    public static void EnableDarkTitleBar(IntPtr hwnd)
    {
        var value = 1;
        try { DwmSetWindowAttribute(hwnd, 20, ref value, sizeof(int)); } catch { }
    }

    public static bool IsElevated()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            var principal = new System.Security.Principal.WindowsPrincipal(identity);
            return principal.IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    public static double CurrentTimerResolutionMs()
    {
        var status = NtQueryTimerResolution(out _, out _, out var current);
        return status == 0 ? current / 10000.0 : -1;
    }
}
