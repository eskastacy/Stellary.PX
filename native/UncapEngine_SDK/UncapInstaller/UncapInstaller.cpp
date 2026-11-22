// ============================================================================
// UncapInstaller.cpp
// Implementation of clean injector & telemetry reader for UncapEngine.dll
// ============================================================================

#include "UncapInstaller.h"

#include <tlhelp32.h>
#include <psapi.h>
#include <algorithm>

#pragma comment(lib, "advapi32.lib")

namespace UncapSDK {

namespace {

std::wstring GetLastErrorString(DWORD errCode) {
    wchar_t* msgBuf = nullptr;
    FormatMessageW(FORMAT_MESSAGE_ALLOCATE_BUFFER | FORMAT_MESSAGE_FROM_SYSTEM | FORMAT_MESSAGE_IGNORE_INSERTS,
                   nullptr, errCode, 0, reinterpret_cast<LPWSTR>(&msgBuf), 0, nullptr);
    std::wstring res = msgBuf ? msgBuf : L"Unknown error";
    if (msgBuf) LocalFree(msgBuf);
    while (!res.empty() && (res.back() == L'\r' || res.back() == L'\n' || res.back() == L' '))
        res.pop_back();
    return res + L" (code " + std::to_wstring(errCode) + L")";
}

} // namespace

bool UncapInstaller::EnableDebugPrivilege() {
    HANDLE token = nullptr;
    if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, &token))
        return false;

    LUID luid{};
    if (!LookupPrivilegeValueW(nullptr, SE_DEBUG_NAME, &luid)) {
        CloseHandle(token);
        return false;
    }

    TOKEN_PRIVILEGES tp{};
    tp.PrivilegeCount = 1;
    tp.Privileges[0].Luid = luid;
    tp.Privileges[0].Attributes = SE_PRIVILEGE_ENABLED;

    BOOL ok = AdjustTokenPrivileges(token, FALSE, &tp, sizeof(tp), nullptr, nullptr);
    DWORD err = GetLastError();
    CloseHandle(token);
    return (ok && err != ERROR_NOT_ALL_ASSIGNED);
}

DWORD UncapInstaller::FindProcessId(const std::wstring& exeName) {
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return 0;

    PROCESSENTRY32W pe{};
    pe.dwSize = sizeof(pe);
    DWORD foundPid = 0;

    std::wstring targetLower = exeName;
    std::transform(targetLower.begin(), targetLower.end(), targetLower.begin(), towlower);

    if (Process32FirstW(snap, &pe)) {
        do {
            std::wstring currentName = pe.szExeFile;
            std::wstring currentLower = currentName;
            std::transform(currentLower.begin(), currentLower.end(), currentLower.begin(), towlower);

            if (currentLower == targetLower || currentLower.find(targetLower) != std::wstring::npos) {
                foundPid = pe.th32ProcessID;
                break;
            }
        } while (Process32NextW(snap, &pe));
    }
    CloseHandle(snap);
    return foundPid;
}

std::vector<ProcessInfo> UncapInstaller::ScanEmulatorProcesses() {
    std::vector<ProcessInfo> list;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPPROCESS, 0);
    if (snap == INVALID_HANDLE_VALUE) return list;

    PROCESSENTRY32W pe{};
    pe.dwSize = sizeof(pe);

    const wchar_t* commonEmulators[] = {
        L"hd-player", L"bluestacks", L"ldplayer", L"dnplayer", L"nox", L"memu"
    };

    if (Process32FirstW(snap, &pe)) {
        do {
            std::wstring lower = pe.szExeFile;
            std::transform(lower.begin(), lower.end(), lower.begin(), towlower);

            bool match = false;
            for (const auto* emu : commonEmulators) {
                if (lower.find(emu) != std::wstring::npos) {
                    match = true;
                    break;
                }
            }
            if (match) {
                ProcessInfo info;
                info.pid = pe.th32ProcessID;
                info.name = pe.szExeFile;

                HANDLE proc = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, pe.th32ProcessID);
                if (proc) {
                    wchar_t fullPath[MAX_PATH]{};
                    DWORD sz = MAX_PATH;
                    if (QueryFullProcessImageNameW(proc, 0, fullPath, &sz))
                        info.fullPath = fullPath;
                    CloseHandle(proc);
                }
                list.push_back(info);
            }
        } while (Process32NextW(snap, &pe));
    }
    CloseHandle(snap);
    return list;
}

bool UncapInstaller::IsInjected(DWORD pid, const std::wstring& dllName) {
    if (pid == 0) return false;
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
    if (snap == INVALID_HANDLE_VALUE) return false;

    MODULEENTRY32W me{};
    me.dwSize = sizeof(me);
    bool found = false;

    std::wstring dllLower = dllName;
    std::transform(dllLower.begin(), dllLower.end(), dllLower.begin(), towlower);

    if (Module32FirstW(snap, &me)) {
        do {
            std::wstring modLower = me.szModule;
            std::transform(modLower.begin(), modLower.end(), modLower.begin(), towlower);
            if (modLower.find(dllLower) != std::wstring::npos) {
                found = true;
                break;
            }
        } while (Module32NextW(snap, &me));
    }
    CloseHandle(snap);
    return found;
}

bool UncapInstaller::Inject(DWORD pid, const std::wstring& dllFullPath, std::wstring* optError) {
    if (pid == 0) {
        if (optError) *optError = L"Invalid process ID (0).";
        return false;
    }

    EnableDebugPrivilege();

    if (GetFileAttributesW(dllFullPath.c_str()) == INVALID_FILE_ATTRIBUTES) {
        if (optError) *optError = L"Payload DLL file not found: " + dllFullPath;
        return false;
    }

    // Open target process with required access
    HANDLE proc = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                              PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                              FALSE, pid);
    if (!proc) {
        DWORD err = GetLastError();
        if (optError) *optError = L"OpenProcess failed: " + GetLastErrorString(err);
        return false;
    }

    // Allocate memory in target process for DLL full path string
    const size_t bytes = (dllFullPath.size() + 1) * sizeof(wchar_t);
    void* remoteStr = VirtualAllocEx(proc, nullptr, bytes, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
    if (!remoteStr) {
        DWORD err = GetLastError();
        CloseHandle(proc);
        if (optError) *optError = L"VirtualAllocEx failed: " + GetLastErrorString(err);
        return false;
    }

    // Write DLL path into target process
    SIZE_T written = 0;
    if (!WriteProcessMemory(proc, remoteStr, dllFullPath.c_str(), bytes, &written) || written != bytes) {
        DWORD err = GetLastError();
        VirtualFreeEx(proc, remoteStr, 0, MEM_RELEASE);
        CloseHandle(proc);
        if (optError) *optError = L"WriteProcessMemory failed: " + GetLastErrorString(err);
        return false;
    }

    // Resolve address of LoadLibraryW
    HMODULE k32 = GetModuleHandleW(L"kernel32.dll");
    auto loadLib = reinterpret_cast<LPTHREAD_START_ROUTINE>(GetProcAddress(k32, "LoadLibraryW"));
    if (!loadLib) {
        VirtualFreeEx(proc, remoteStr, 0, MEM_RELEASE);
        CloseHandle(proc);
        if (optError) *optError = L"Failed to resolve LoadLibraryW in kernel32.";
        return false;
    }

    // Launch remote thread to call LoadLibraryW(remoteStr)
    HANDLE thr = CreateRemoteThread(proc, nullptr, 0, loadLib, remoteStr, 0, nullptr);
    if (!thr) {
        // Fallback: try RtlCreateUserThread from ntdll if CreateRemoteThread was blocked
        using RtlCreateUserThread_t = LONG(NTAPI*)(HANDLE, PSECURITY_DESCRIPTOR, BOOLEAN, ULONG,
                                                    SIZE_T*, SIZE_T*, PVOID, PVOID, PHANDLE, PVOID);
        auto rtlCreate = reinterpret_cast<RtlCreateUserThread_t>(
            GetProcAddress(GetModuleHandleW(L"ntdll.dll"), "RtlCreateUserThread"));
        if (rtlCreate) {
            HANDLE t2 = nullptr;
            if (rtlCreate(proc, nullptr, FALSE, 0, nullptr, nullptr,
                          reinterpret_cast<PVOID>(loadLib), remoteStr, &t2, nullptr) >= 0) {
                thr = t2;
            }
        }
    }

    if (!thr) {
        DWORD err = GetLastError();
        VirtualFreeEx(proc, remoteStr, 0, MEM_RELEASE);
        CloseHandle(proc);
        if (optError) *optError = L"CreateRemoteThread failed: " + GetLastErrorString(err);
        return false;
    }

    // Wait up to 10 seconds for the DLL to load and initialize
    WaitForSingleObject(thr, 10000);
    DWORD exitCode = 0;
    GetExitCodeThread(thr, &exitCode);
    CloseHandle(thr);
    CloseHandle(proc);

    if (exitCode == 0) {
        if (optError) *optError = L"LoadLibraryW returned NULL inside the target process.";
        return false;
    }

    return true;
}

bool UncapInstaller::Eject(DWORD pid, const std::wstring& dllFullPath, std::wstring* optError) {
    if (pid == 0) return false;
    EnableDebugPrivilege();

    // Find remote module handle
    HANDLE snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, pid);
    if (snap == INVALID_HANDLE_VALUE) return false;

    MODULEENTRY32W me{};
    me.dwSize = sizeof(me);
    HMODULE remoteMod = nullptr;

    wchar_t dllFilename[MAX_PATH]{};
    _wsplitpath_s(dllFullPath.c_str(), nullptr, 0, nullptr, 0, dllFilename, MAX_PATH, nullptr, 0);
    std::wstring targetFilename = dllFilename;
    std::transform(targetFilename.begin(), targetFilename.end(), targetFilename.begin(), towlower);

    if (Module32FirstW(snap, &me)) {
        do {
            std::wstring mod = me.szModule;
            std::transform(mod.begin(), mod.end(), mod.begin(), towlower);
            if (mod.find(targetFilename) != std::wstring::npos) {
                remoteMod = me.hModule;
                break;
            }
        } while (Module32NextW(snap, &me));
    }
    CloseHandle(snap);

    if (!remoteMod) {
        if (optError) *optError = L"Module is not currently loaded in the target process.";
        return true;
    }

    // Resolve RVA of OptimaizerUnhook
    LPTHREAD_START_ROUTINE remoteUnhook = nullptr;
    HMODULE localImg = LoadLibraryExW(dllFullPath.c_str(), nullptr, LOAD_LIBRARY_AS_DATAFILE);
    if (localImg) {
        FARPROC fn = GetProcAddress(localImg, "OptimaizerUnhook");
        if (fn) {
            uintptr_t rva = reinterpret_cast<uintptr_t>(fn) - reinterpret_cast<uintptr_t>(localImg);
            remoteUnhook = reinterpret_cast<LPTHREAD_START_ROUTINE>(reinterpret_cast<uintptr_t>(remoteMod) + rva);
        }
        FreeLibrary(localImg);
    }

    HANDLE proc = OpenProcess(PROCESS_CREATE_THREAD | PROCESS_QUERY_INFORMATION |
                              PROCESS_VM_OPERATION | PROCESS_VM_WRITE | PROCESS_VM_READ,
                              FALSE, pid);
    if (!proc) {
        if (optError) *optError = L"OpenProcess failed.";
        return false;
    }

    if (remoteUnhook) {
        HANDLE thr = CreateRemoteThread(proc, nullptr, 0, remoteUnhook, nullptr, 0, nullptr);
        if (thr) {
            WaitForSingleObject(thr, 5000);
            CloseHandle(thr);
        }
    }

    CloseHandle(proc);
    return true;
}

TelemetryData UncapInstaller::ReadTelemetry(DWORD pid) {
    TelemetryData data;
    if (pid == 0) return data;

    std::wstring mapName = L"Local\\OptimaizerFps_" + std::to_wstring(pid);
    HANDLE hMap = OpenFileMappingW(FILE_MAP_READ, FALSE, mapName.c_str());
    if (!hMap) return data;

    void* view = MapViewOfFile(hMap, FILE_MAP_READ, 0, 0, sizeof(uncap::SharedMemory));
    if (!view) {
        CloseHandle(hMap);
        return data;
    }

    uncap::SharedMemory snap{};
    memcpy(&snap, view, sizeof(uncap::SharedMemory));
    UnmapViewOfFile(view);
    CloseHandle(hMap);

    if (memcmp(snap.magic, uncap::kMagic, sizeof(uncap::kMagic)) != 0 ||
        snap.version != uncap::kVersion) {
        return data;
    }

    data.valid        = true;
    data.avgMs        = snap.avgMs;
    data.minMs        = snap.minMs;
    data.maxMs        = snap.maxMs;
    data.p95Ms        = snap.p95Ms;
    data.frames       = snap.frames;
    data.dropped      = snap.dropped;
    data.flags        = snap.flags;
    data.isUncapped   = (snap.flags & uncap::kFlagUncapped) != 0;
    data.isFakeActive = (snap.flags & uncap::kFlagFakeFpsActive) != 0;

    if (snap.avgMs > 0.0001)
        data.currentFps = 1000.0 / snap.avgMs;

    return data;
}

bool UncapInstaller::WriteConfig(const std::wstring& iniPath, int multiplier, bool enabled, bool jitter) {
    WritePrivateProfileStringW(L"FakeFps", L"Enabled", enabled ? L"1" : L"0", iniPath.c_str());
    WritePrivateProfileStringW(L"FakeFps", L"Multiplier", std::to_wstring(multiplier).c_str(), iniPath.c_str());
    WritePrivateProfileStringW(L"FakeFps", L"Jitter", jitter ? L"1" : L"0", iniPath.c_str());
    return true;
}

} // namespace UncapSDK
