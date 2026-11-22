#pragma once
// ============================================================================
// UncapInstaller.h
// Clean, Modular Injector & Pacer Controller for external projects.
// Drop this header and UncapInstaller.cpp into any C++ project!
// ============================================================================

#include "../SharedContract/UncapTelemetry.h"

#include <windows.h>
#include <string>
#include <vector>

namespace UncapSDK {

struct ProcessInfo {
    DWORD        pid = 0;
    std::wstring name;
    std::wstring fullPath;
};

struct TelemetryData {
    bool    valid        = false;
    double  currentFps   = 0.0;
    double  avgMs        = 0.0;
    double  minMs        = 0.0;
    double  maxMs        = 0.0;
    double  p95Ms        = 0.0;
    uint64_t frames      = 0;
    uint64_t dropped     = 0;
    uint32_t flags       = 0;
    bool    isUncapped   = false;
    bool    isFakeActive = false;
};

class UncapInstaller {
public:
    // 1. Privilege & Process Management
    static bool EnableDebugPrivilege();
    static DWORD FindProcessId(const std::wstring& exeName = L"HD-Player.exe");
    static std::vector<ProcessInfo> ScanEmulatorProcesses();

    // 2. DLL Injection & Ejection
    static bool IsInjected(DWORD pid, const std::wstring& dllName = L"UncapEngine.dll");
    static bool Inject(DWORD pid, const std::wstring& dllFullPath, std::wstring* optError = nullptr);
    static bool Eject(DWORD pid, const std::wstring& dllFullPath, std::wstring* optError = nullptr);

    // 3. Telemetry & Metric Reading
    static TelemetryData ReadTelemetry(DWORD pid);

    // 4. Configuration Helper
    //    multiplier: overlay = multiplier x the FPS BlueStacks actually ran at (1..10)
    static bool WriteConfig(const std::wstring& iniPath, int multiplier = 2, bool enabled = true, bool jitter = true);
};

} // namespace UncapSDK
