using System.Runtime.InteropServices;
using System.Text;

namespace Stellary.PX.Infrastructure;

public static class NativeMethods
{
    private const uint ProcessQueryLimitedInformation = 0x1000;
    private const uint ProcessSetInformation = 0x0200;

    private const uint IdlePriorityClass = 0x00000040;
    private const uint BelowNormalPriorityClass = 0x00004000;
    private const uint NormalPriorityClass = 0x00000020;
    private const uint AboveNormalPriorityClass = 0x00008000;
    private const uint HighPriorityClass = 0x00000080;
    private const uint RealtimePriorityClass = 0x00000100;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryFullProcessImageName(
        IntPtr process, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint GetPriorityClass(IntPtr process);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetPriorityClass(IntPtr process, uint priorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

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

    /// <summary>Reads the image path with limited-query rights, which BlueStacks still permits.</summary>
    public static string? TryGetProcessPath(int pid)
    {
        var handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (handle == IntPtr.Zero) return null;

            var buffer = new StringBuilder(1024);
            var size = (uint)buffer.Capacity;
            return QueryFullProcessImageName(handle, 0, buffer, ref size)
                ? buffer.ToString()
                : null;
        }
        catch
        {
            return null;
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    /// <summary>Reads the current priority class; returns "protected" when the target denies even a query.</summary>
    public static string TryGetPriority(int pid)
    {
        var handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessQueryLimitedInformation, false, pid);
            if (handle == IntPtr.Zero) return "protected";

            return GetPriorityClass(handle) switch
            {
                IdlePriorityClass => "Idle",
                BelowNormalPriorityClass => "Below normal",
                NormalPriorityClass => "Normal",
                AboveNormalPriorityClass => "Above normal",
                HighPriorityClass => "High",
                RealtimePriorityClass => "Realtime",
                _ => "unknown"
            };
        }
        catch
        {
            return "unknown";
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

    /// <summary>
    /// Sets the priority class and returns the Win32 error instead of throwing.
    /// BlueStacks 5.22 denies PROCESS_SET_INFORMATION even to elevated admins,
    /// which surfaces here as error 5.
    /// </summary>
    public static (bool Ok, int Error) TrySetPriority(int pid, bool aboveNormal)
    {
        var handle = IntPtr.Zero;
        try
        {
            handle = OpenProcess(ProcessSetInformation, false, pid);
            if (handle == IntPtr.Zero) return (false, Marshal.GetLastWin32Error());

            var ok = SetPriorityClass(handle, aboveNormal ? AboveNormalPriorityClass : NormalPriorityClass);
            return (ok, ok ? 0 : Marshal.GetLastWin32Error());
        }
        catch
        {
            return (false, -1);
        }
        finally
        {
            if (handle != IntPtr.Zero) CloseHandle(handle);
        }
    }

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
