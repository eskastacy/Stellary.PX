using System.Buffers.Binary;
using System.Diagnostics;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
using Stellary.PX.Core;
using Stellary.PX.Infrastructure;

namespace Stellary.PX.Services;

/// <summary>
/// C# port of the UncapEngine SDK installer: injects UncapEngine.dll into the
/// emulator, reads its shared-memory telemetry, and disarms/re-arms it through
/// the OptimaizerUnhook / OptimaizerHook exports.
/// </summary>
public sealed class UncapService
{
    public const string DllName = "UncapEngine.dll";
    public const string IniName = "UncapEngine.ini";
    public const int DefaultMultiplier = 2;

    /// <summary>sizeof(uncap::SharedMemory) from UncapTelemetry.h, Pack = 8.</summary>
    public const int SharedMemorySize = 2168;

    private static readonly byte[] Magic = "OPTFPS01"u8.ToArray();
    private const uint TelemetryVersion = 3;
    private const uint FlagUncapped = 1u << 6;
    private const uint FlagFakeActive = 1u << 9;
    private const uint FlagPriorityAbove = 1u << 10;
    private const uint FlagArmed = 1u << 11;
    private const uint FlagHookMask = (1u << 0) | (1u << 1) | (1u << 7) | (1u << 8);

    public static string DllPath => Path.Combine(AppContext.BaseDirectory, DllName);
    public static string IniPath => Path.Combine(AppContext.BaseDirectory, IniName);

    public sealed record Telemetry(
        bool Valid,
        double CurrentFps,
        double AvgMs,
        double MinMs,
        double MaxMs,
        double P95Ms,
        ulong Frames,
        ulong Dropped,
        uint Flags)
    {
        public bool IsUncapped => (Flags & FlagUncapped) != 0;
        public bool IsFakeActive => (Flags & FlagFakeActive) != 0;
        public bool IsArmed => (Flags & FlagArmed) != 0;
        public bool IsPriorityAbove => (Flags & FlagPriorityAbove) != 0;
    }

    public StepResult Inject(int pid, int multiplier = DefaultMultiplier)
    {
        if (pid == 0) return StepResult.Fail("Uncap", "Target process not running.");
        if (!File.Exists(DllPath)) return StepResult.Fail("Uncap", $"Payload missing: {DllPath}");
        if (!NativeMethods.IsElevated()) return StepResult.Fail("Uncap", "Administrator required to inject.");

        EnableDebugPrivilege();
        WriteConfig(IniPath, multiplier);

        if (ReadTelemetry(pid).Valid)
        {
            // The module is already resident and its export is out of reach
            // through OpenProcess; re-arm it through the message hook instead.
            var rearmed = CallExportViaHook(pid, "OptimaizerHook") ||
                          (FindModule(pid, DllName, out var loadedBase) &&
                           CallExport(pid, loadedBase, "OptimaizerHook"));

            // OptimaizerHook queues the hook work and sets the armed flag
            // immediately, so the flag — not the hook bits — says it was
            // accepted. The worker publishes the hook bits moments later.
            if (!rearmed) rearmed = (ReadTelemetry(pid).Flags & FlagArmed) != 0;

            return rearmed
                ? StepResult.Skip("Uncap", "Engine already loaded · re-armed · tracking the live rate at 2×.")
                : StepResult.Fail("Uncap", "Engine already loaded, but re-arm failed.");
        }

        // The new BlueStacks driver strips PROCESS_VM_* and PROCESS_CREATE_THREAD
        // from every handle, so the loader runs through a thread-scoped message
        // hook: the system maps the DLL without OpenProcess at all.
        var (hooked, hookError) = InjectViaHook(pid);
        if (hooked)
        {
            var hookedTel = ReadTelemetry(pid);
            return StepResult.Success("Uncap", hookedTel.Valid
                ? $"Injected · {FpsText(hookedTel)}"
                : "Injected · waiting for the emulator's first frame.");
        }

        // Older drivers still allow the classic remote-thread path.
        var (ok, error) = InjectDll(pid, DllPath);
        if (!ok) return StepResult.Fail("Uncap", hookError is not null ? $"{hookError} · {error}" : error ?? "Injection failed.");

        var tel = ReadTelemetry(pid);
        return StepResult.Success("Uncap", tel.Valid
            ? $"Injected · {FpsText(tel)}"
            : "Injected · waiting for the emulator's first frame.");
    }

    public StepResult Eject(int pid)
    {
        if (pid == 0) return StepResult.Fail("Uncap", "Target process not running.");
        if (!ReadTelemetry(pid).Valid && !FindModule(pid, DllName, out _))
            return StepResult.Skip("Uncap", "Engine is not injected.");

        EnableDebugPrivilege();
        var ejected = CallExportViaHook(pid, "OptimaizerUnhook");
        if (!ejected && FindModule(pid, DllName, out var moduleBase))
            ejected = CallExport(pid, moduleBase, "OptimaizerUnhook");

        // Same timeout caveat as re-arm: the export may have run after the wait.
        if (!ejected)
        {
            var after = ReadTelemetry(pid);
            ejected = after.Valid && (after.Flags & FlagHookMask) == 0;
        }

        return ejected
            ? StepResult.Success("Uncap", "Hooks removed · engine left loaded until BlueStacks exits.")
            : StepResult.Fail("Uncap", "Could not call OptimaizerUnhook.");
    }

    public bool IsInjected(int pid) => ReadTelemetry(pid).Valid || FindModule(pid, DllName, out _);

    /// <summary>
    /// Sets the priority class from inside the target through
    /// OptimaizerSetPriority. The export runs on the emulator UI thread via
    /// WH_CALLWNDPROC, so the target's own token is used and no
    /// PROCESS_SET_INFORMATION handle is needed.
    /// </summary>
    public StepResult SetPriority(int pid, bool aboveNormal)
    {
        if (pid == 0) return StepResult.Fail("Emulator priority", "Target process not running.");
        if (!ReadTelemetry(pid).Valid && !FindModule(pid, DllName, out _))
            return StepResult.Fail("Emulator priority", "Engine is not loaded in the target.");

        EnableDebugPrivilege();
        var sent = CallExportViaHook(pid, "OptimaizerSetPriority",
            aboveNormal ? new IntPtr(1) : IntPtr.Zero);

        // The hook can run after SendMessageTimeout gives up on a busy UI
        // thread. The shared flag is the source of truth.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var tel = ReadTelemetry(pid);
            if (tel.Valid && ((tel.Flags & FlagPriorityAbove) != 0) == aboveNormal)
                return StepResult.Success("Emulator priority",
                    aboveNormal ? "Above normal · set in-process" : "Normal · set in-process");
            Thread.Sleep(50);
        }

        return sent
            ? StepResult.Fail("Emulator priority", "Export ran but the priority flag did not change.")
            : StepResult.Fail("Emulator priority", "Could not reach OptimaizerSetPriority.");
    }

    public Telemetry ReadTelemetry(int pid)
    {
        if (pid == 0) return Empty();
        try
        {
            using var map = MemoryMappedFile.OpenExisting($"Local\\OptimaizerFps_{pid}", MemoryMappedFileRights.Read);
            using var view = map.CreateViewAccessor(0, SharedMemorySize, MemoryMappedFileAccess.Read);
            var buffer = new byte[SharedMemorySize];
            view.ReadArray(0, buffer, 0, buffer.Length);

            var span = buffer.AsSpan();
            if (!span[..8].SequenceEqual(Magic)) return Empty();
            if (BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(8, 4)) != TelemetryVersion) return Empty();
            if (BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(12, 4)) != SharedMemorySize) return Empty();

            var flags = BinaryPrimitives.ReadUInt32LittleEndian(span.Slice(16, 4));
            var frames = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(40, 8));
            var dropped = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(48, 8));
            var avgMs = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(56, 8));
            var p95Ms = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(64, 8));
            var minMs = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(72, 8));
            var maxMs = BinaryPrimitives.ReadDoubleLittleEndian(span.Slice(80, 8));

            return new Telemetry(true, avgMs > 0.0001 ? 1000.0 / avgMs : 0, avgMs, minMs, maxMs,
                p95Ms, frames, dropped, flags);
        }
        catch
        {
            return Empty();
        }
    }

    public static string FpsText(Telemetry tel) =>
        $"{tel.CurrentFps:0} FPS · avg {tel.AvgMs:0.00} ms · p95 {tel.P95Ms:0.00} ms";

    private static Telemetry Empty() => new(false, 0, 0, 0, 0, 0, 0, 0, 0);

    // ------------------------------------------------------ handle-free path

    /// <summary>
    /// Loads the payload through a thread-scoped WH_GETMESSAGE hook. The system
    /// maps the DLL for the hook without OpenProcess, which is the only route
    /// left once BstkProtect strips PROCESS_VM_* and PROCESS_CREATE_THREAD.
    /// </summary>
    private (bool Ok, string? Error) InjectViaHook(int pid)
    {
        if (!TryGetGuiThread(pid, out var tid, out _))
            return (false, "No GUI thread in the target window.");

        var image = LoadLibraryEx(DllPath, IntPtr.Zero, DontResolveReferences);
        if (image == IntPtr.Zero)
            return (false, "Local map failed: " + LastError());

        try
        {
            var gadget = GadgetRva(DllPath);
            if (gadget is null) return (false, "No in-module xor/ret gadget found.");

            var proc = IntPtr.Add(image, (int)gadget.Value);
            var hook = SetWindowsHookEx(WhGetMessage, proc, image, tid);
            if (hook == IntPtr.Zero)
                return (false, "SetWindowsHookEx failed: " + LastError());

            try
            {
                if (!PostThreadMessage(tid, WmNull, IntPtr.Zero, IntPtr.Zero))
                    return (false, "PostThreadMessage failed: " + LastError());

                // The engine boots from DllMain and publishes its mapping before
                // the first message returns; poll for it instead of FindModule,
                // which cannot see modules inside a protected process.
                for (var attempt = 0; attempt < 40; attempt++)
                {
                    Thread.Sleep(100);
                    if (ReadTelemetry(pid).Valid) return (true, null);
                }

                return (false, "Engine mapped but published no telemetry.");
            }
            finally
            {
                UnhookWindowsHookEx(hook);
            }
        }
        finally
        {
            // DONT_RESOLVE_DLL_REFERENCES never ran DllMain locally, so dropping
            // this reference after unhooking cannot unload the target's copy.
            FreeLibrary(image);
        }
    }

    /// <summary>
    /// Calls an export in the target through a message hook. WH_CALLWNDPROC runs
    /// the procedure in the target's UI thread; the return value is ignored, so
    /// the exports are written as plain void/int entry points. The message wParam
    /// reaches the export through the CWPSTRUCT (see OptimaizerSetPriority).
    /// </summary>
    private static bool CallExportViaHook(int pid, string export, IntPtr wParam = default)
    {
        if (!TryGetGuiThread(pid, out var tid, out var hwnd)) return false;

        var image = LoadLibraryEx(DllPath, IntPtr.Zero, DontResolveReferences);
        if (image == IntPtr.Zero) return false;

        try
        {
            var proc = GetProcAddress(image, export);
            if (proc == IntPtr.Zero) return false;

            var hook = SetWindowsHookEx(WhCallWndProc, proc, image, tid);
            if (hook == IntPtr.Zero) return false;

            try
            {
                // Short timeout: this call runs on the UI thread that requested
                // the action, and a hung emulator must not hang the console.
                var sent = SendMessageTimeout(hwnd, WmNull, wParam, IntPtr.Zero,
                    SmtoAbortIfHung, 2_500, out _);
                return sent != IntPtr.Zero;
            }
            finally
            {
                UnhookWindowsHookEx(hook);
            }
        }
        finally
        {
            FreeLibrary(image);
        }
    }

    private static bool TryGetGuiThread(int pid, out uint tid, out IntPtr hwnd)
    {
        tid = 0;
        hwnd = IntPtr.Zero;
        try
        {
            using var process = Process.GetProcessById(pid);
            hwnd = process.MainWindowHandle;
        }
        catch
        {
            return false;
        }

        if (hwnd == IntPtr.Zero) return false;
        tid = GetWindowThreadProcessId(hwnd, out _);
        return tid != 0;
    }

    /// <summary>
    /// First zero-return gadget in an executable section: `xor eax,eax; ret`,
    /// encoded either as 33 C0 C3 (MSVC) or 31 C0 C3 (GCC).
    /// </summary>
    public static uint? GadgetRva(string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            if (bytes.Length < 0x40) return null;

            var pe = BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(0x3C));
            if (pe <= 0 || pe + 24 > bytes.Length) return null;

            var sections = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pe + 6));
            var optionalSize = BinaryPrimitives.ReadUInt16LittleEndian(bytes.AsSpan(pe + 20));
            var optional = pe + 24;
            var table = optional + optionalSize;

            for (var i = 0; i < sections; i++)
            {
                var entry = table + i * 40;
                if (entry + 40 > bytes.Length) return null;

                var virtualSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 8));
                var virtualAddress = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 12));
                var rawSize = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 16));
                var raw = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 20));
                var characteristics = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(entry + 36));
                if ((characteristics & 0x20000000) == 0) continue;

                var end = (long)raw + Math.Min(virtualSize, rawSize);
                for (long offset = raw; offset + 3 <= end && offset + 3 <= bytes.Length; offset++)
                {
                    if (!IsReturnZeroGadget(bytes, offset)) continue;
                    return virtualAddress + (uint)(offset - raw);
                }
            }
        }
        catch
        {
            // A locked payload is reported as "no gadget" and the caller falls back.
        }

        return null;
    }

    private static bool IsReturnZeroGadget(byte[] bytes, long offset) =>
        (bytes[offset] == 0x33 || bytes[offset] == 0x31) &&
        bytes[offset + 1] == 0xC0 && bytes[offset + 2] == 0xC3;

    // ------------------------------------------------------------- injection

    private static (bool Ok, string? Error) InjectDll(int pid, string dllPath)
    {
        const uint access = ProcessCreateThread | ProcessQueryInformation |
                            ProcessVmOperation | ProcessVmWrite | ProcessVmRead;

        var process = OpenProcess(access, false, pid);
        if (process == IntPtr.Zero) return (false, "OpenProcess failed: " + LastError());

        var bytes = System.Text.Encoding.Unicode.GetBytes(dllPath + "\0");
        var remote = VirtualAllocEx(process, IntPtr.Zero, (nuint)bytes.Length,
            MemCommit | MemReserve, PageReadWrite);
        if (remote == IntPtr.Zero)
        {
            CloseHandle(process);
            return (false, "VirtualAllocEx failed: " + LastError());
        }

        try
        {
            if (!WriteProcessMemory(process, remote, bytes, (nuint)bytes.Length, out var written) ||
                written != (nuint)bytes.Length)
                return (false, "WriteProcessMemory failed: " + LastError());

            var kernel = GetModuleHandle("kernel32.dll");
            var loadLibrary = GetProcAddress(kernel, "LoadLibraryW");
            if (loadLibrary == IntPtr.Zero) return (false, "LoadLibraryW not found.");

            var thread = CreateRemoteThread(process, IntPtr.Zero, 0, loadLibrary, remote, 0, IntPtr.Zero);
            if (thread == IntPtr.Zero) thread = CreateUserThread(process, loadLibrary, remote);
            if (thread == IntPtr.Zero) return (false, "CreateRemoteThread failed: " + LastError());

            try
            {
                WaitForSingleObject(thread, 10_000);
                GetExitCodeThread(thread, out var exitCode);
                return exitCode == 0
                    ? (false, "LoadLibraryW returned NULL inside the target process.")
                    : (true, null);
            }
            finally
            {
                CloseHandle(thread);
            }
        }
        finally
        {
            VirtualFreeEx(process, remote, 0, MemRelease);
            CloseHandle(process);
        }
    }

    private static bool CallExport(int pid, IntPtr moduleBase, string export)
    {
        var rva = ExportRva(DllPath, export);
        if (rva is null) return false;

        const uint access = ProcessCreateThread | ProcessQueryInformation |
                            ProcessVmOperation | ProcessVmWrite | ProcessVmRead;
        var process = OpenProcess(access, false, pid);
        if (process == IntPtr.Zero) return false;

        try
        {
            var address = IntPtr.Add(moduleBase, (int)rva.Value);
            var thread = CreateRemoteThread(process, IntPtr.Zero, 0, address, IntPtr.Zero, 0, IntPtr.Zero);
            if (thread == IntPtr.Zero) thread = CreateUserThread(process, address, IntPtr.Zero);
            if (thread == IntPtr.Zero) return false;

            try
            {
                return WaitForSingleObject(thread, 5_000) == WaitObject0;
            }
            finally
            {
                CloseHandle(thread);
            }
        }
        finally
        {
            CloseHandle(process);
        }
    }

    private static IntPtr CreateUserThread(IntPtr process, IntPtr start, IntPtr parameter)
    {
        // Same fallback the C++ installer uses when CreateRemoteThread is blocked.
        var status = RtlCreateUserThread(process, IntPtr.Zero, false, 0, IntPtr.Zero, IntPtr.Zero,
            start, parameter, out var thread, IntPtr.Zero);
        return status >= 0 ? thread : IntPtr.Zero;
    }

    private static bool FindModule(int pid, string name, out IntPtr moduleBase)
    {
        moduleBase = IntPtr.Zero;
        if (pid == 0) return false;

        var snapshot = CreateToolhelp32Snapshot(SnapModule | SnapModule32, (uint)pid);
        if (snapshot == InvalidHandle) return false;

        try
        {
            var entry = new ModuleEntry { Size = (uint)Marshal.SizeOf<ModuleEntry>() };
            if (!Module32First(snapshot, ref entry)) return false;
            do
            {
                if (!string.Equals(entry.Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                moduleBase = entry.Module;
                return true;
            } while (Module32Next(snapshot, ref entry));
        }
        finally
        {
            CloseHandle(snapshot);
        }

        return false;
    }

    // ------------------------------------------------------- telemetry config

    public static bool WriteConfig(string iniPath, int multiplier = DefaultMultiplier)
    {
        try
        {
            var dir = Path.GetDirectoryName(iniPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            WritePrivateProfileString("FakeFps", "Enabled", "1", iniPath);
            WritePrivateProfileString("FakeFps", "Multiplier", Math.Clamp(multiplier, 1, 10).ToString(), iniPath);
            WritePrivateProfileString("FakeFps", "Jitter", "1", iniPath);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void EnableDebugPrivilege()
    {
        if (!OpenProcessToken(GetCurrentProcess(), TokenAdjustPrivileges | TokenQuery, out var token)) return;
        try
        {
            if (!LookupPrivilegeValue(null, "SeDebugPrivilege", out var luid)) return;
            var privileges = new TokenPrivileges
            {
                Count = 1,
                Privilege = new LuidAndAttributes { Luid = luid, Attributes = SePrivilegeEnabled }
            };
            AdjustTokenPrivileges(token, false, ref privileges, 0, IntPtr.Zero, IntPtr.Zero);
        }
        finally
        {
            CloseHandle(token);
        }
    }

    private static string LastError() => Marshal.GetLastWin32Error().ToString();

    // --------------------------------------------------------- PE export RVA

    public static uint? ExportRva(string path, string name)
    {
        try
        {
            using var stream = File.OpenRead(path);
            using var reader = new BinaryReader(stream);
            if (stream.Length < 0x40 || reader.ReadUInt16() != 0x5A4D) return null;

            stream.Position = 0x3C;
            var pe = reader.ReadInt32();
            if (pe <= 0 || pe + 24 > stream.Length) return null;

            stream.Position = pe;
            if (reader.ReadUInt32() != 0x00004550) return null;

            stream.Position = pe + 6;
            var sectionCount = reader.ReadUInt16();
            stream.Position = pe + 20;
            var optionalSize = reader.ReadUInt16();
            var optional = pe + 24;
            stream.Position = optional;
            var magic = reader.ReadUInt16();
            var dataDir = optional + (magic == 0x20B ? 112 : 96);
            stream.Position = dataDir;
            var exportRva = reader.ReadUInt32();
            reader.ReadUInt32(); // export size
            if (exportRva == 0) return null;

            var sections = new (uint Va, uint Size, uint Raw)[sectionCount];
            var sectionOffset = optional + optionalSize;
            for (var i = 0; i < sectionCount; i++)
            {
                stream.Position = sectionOffset + i * 40 + 8;
                var virtualSize = reader.ReadUInt32();
                var virtualAddress = reader.ReadUInt32();
                reader.ReadUInt32(); // raw size
                var raw = reader.ReadUInt32();
                sections[i] = (virtualAddress, virtualSize, raw);
            }

            long RvaToOffset(uint rva)
            {
                foreach (var section in sections)
                {
                    if (rva < section.Va || rva >= section.Va + section.Size) continue;
                    return section.Raw + (rva - section.Va);
                }
                return -1;
            }

            var directory = RvaToOffset(exportRva);
            if (directory < 0) return null;

            stream.Position = directory + 24;
            var nameCount = reader.ReadUInt32();
            stream.Position = directory + 28;
            var functions = RvaToOffset(reader.ReadUInt32());
            stream.Position = directory + 32;
            var names = RvaToOffset(reader.ReadUInt32());
            stream.Position = directory + 36;
            var ordinals = RvaToOffset(reader.ReadUInt32());
            if (functions < 0 || names < 0 || ordinals < 0) return null;

            for (var i = 0u; i < nameCount; i++)
            {
                stream.Position = names + i * 4;
                var nameOffset = RvaToOffset(reader.ReadUInt32());
                if (nameOffset < 0) continue;

                stream.Position = nameOffset;
                var text = new System.Text.StringBuilder();
                for (var guard = 0; guard < 256; guard++)
                {
                    var ch = reader.ReadByte();
                    if (ch == 0) break;
                    text.Append((char)ch);
                }

                if (!string.Equals(text.ToString(), name, StringComparison.Ordinal)) continue;

                stream.Position = ordinals + i * 2;
                var ordinal = reader.ReadUInt16();
                stream.Position = functions + ordinal * 4;
                return reader.ReadUInt32();
            }
        }
        catch
        {
            // A truncated or locked payload is reported as "export not found".
        }

        return null;
    }

    // ------------------------------------------------------------- P/Invoke

    private const uint ProcessCreateThread = 0x0002;
    private const uint ProcessQueryInformation = 0x0400;
    private const uint ProcessVmOperation = 0x0008;
    private const uint ProcessVmWrite = 0x0020;
    private const uint ProcessVmRead = 0x0010;

    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;
    private const uint MemRelease = 0x8000;
    private const uint PageReadWrite = 0x04;

    private const uint SnapModule = 0x00000008;
    private const uint SnapModule32 = 0x00000010;
    private static readonly IntPtr InvalidHandle = new(-1);
    private const uint WaitObject0 = 0;

    private const uint DontResolveReferences = 0x00000001;
    private const int WhGetMessage = 3;
    private const int WhCallWndProc = 4;
    private const uint WmNull = 0x0000;
    private const uint SmtoAbortIfHung = 0x0002;

    private const uint TokenAdjustPrivileges = 0x0020;
    private const uint TokenQuery = 0x0008;
    private const uint SePrivilegeEnabled = 0x0002;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct ModuleEntry
    {
        public uint Size;
        public uint ModuleId;
        public uint ProcessId;
        public uint GlobalUsage;
        public uint ProcessUsage;
        public IntPtr BaseAddress;
        public uint BaseSize;
        public IntPtr Module;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string Name;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string Path;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Luid
    {
        public uint LowPart;
        public int HighPart;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct LuidAndAttributes
    {
        public Luid Luid;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges
    {
        public uint Count;
        public LuidAndAttributes Privilege;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr LoadLibraryEx(string path, IntPtr file, uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool FreeLibrary(IntPtr module);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SetWindowsHookEx(int id, IntPtr procedure, IntPtr module, uint threadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnhookWindowsHookEx(IntPtr hook);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool PostThreadMessage(uint threadId, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeout(IntPtr window, uint message, IntPtr wParam, IntPtr lParam,
        uint flags, uint timeout, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr OpenProcess(uint access, [MarshalAs(UnmanagedType.Bool)] bool inherit, int pid);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAllocEx(IntPtr process, IntPtr address, nuint size, uint type, uint protect);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VirtualFreeEx(IntPtr process, IntPtr address, nuint size, uint type);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WriteProcessMemory(IntPtr process, IntPtr address, byte[] buffer, nuint size,
        out nuint written);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr GetModuleHandle(string name);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern IntPtr GetProcAddress(IntPtr module, string name);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateRemoteThread(IntPtr process, IntPtr attributes, uint stackSize,
        IntPtr start, IntPtr parameter, uint flags, IntPtr threadId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetExitCodeThread(IntPtr thread, out uint exitCode);

    [DllImport("ntdll.dll")]
    private static extern int RtlCreateUserThread(IntPtr process, IntPtr descriptor,
        [MarshalAs(UnmanagedType.U1)] bool suspended, uint zeroBits, IntPtr reserved, IntPtr commit,
        IntPtr start, IntPtr parameter, out IntPtr thread, IntPtr clientId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32First(IntPtr snapshot, ref ModuleEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool Module32Next(IntPtr snapshot, ref ModuleEntry entry);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WritePrivateProfileString(string section, string key, string value, string file);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool OpenProcessToken(IntPtr process, uint access, out IntPtr token);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LookupPrivilegeValue(string? system, string name, out Luid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool AdjustTokenPrivileges(IntPtr token,
        [MarshalAs(UnmanagedType.Bool)] bool disableAll, ref TokenPrivileges state, uint length,
        IntPtr previous, IntPtr returnLength);
}
