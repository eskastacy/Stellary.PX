// ============================================================================
// Example_Main.cpp
// Standalone Example showing how to use UncapInstaller in ANY other C++ project!
// ============================================================================

#include "UncapInstaller/UncapInstaller.h"

#include <iostream>
#include <chrono>
#include <thread>
#include <conio.h>

std::wstring GetCurrentExecutableDir() {
    wchar_t path[MAX_PATH]{};
    GetModuleFileNameW(nullptr, path, MAX_PATH);
    wchar_t* slash = wcsrchr(path, L'\\');
    if (slash) *slash = 0;
    return path;
}

int main() {
    std::wcout << L"====================================================\n";
    std::wcout << L"      UNCAP ENGINE SDK - INTEGRATION EXAMPLE        \n";
    std::wcout << L"====================================================\n\n";

    // 1. Enable Debug Privilege
    if (!UncapSDK::UncapInstaller::EnableDebugPrivilege()) {
        std::wcout << L"[!] Warning: Could not enable SeDebugPrivilege (run as Administrator if needed).\n";
    }

    // 2. Locate Emulator Process (e.g., HD-Player.exe / BlueStacks)
    std::wcout << L"[*] Scanning for BlueStacks / HD-Player.exe...\n";
    DWORD pid = UncapSDK::UncapInstaller::FindProcessId(L"HD-Player.exe");
    if (pid == 0) {
        std::wcout << L"[-] HD-Player.exe not found! Scanning for any emulator...\n";
        auto emus = UncapSDK::UncapInstaller::ScanEmulatorProcesses();
        if (!emus.empty()) {
            pid = emus[0].pid;
            std::wcout << L"[+] Detected alternative emulator: " << emus[0].name << L" [PID " << pid << L"]\n";
        }
    }

    if (pid == 0) {
        std::wcout << L"[-] No running emulator found. Please start BlueStacks first and run this tool again.\n";
        std::wcout << L"Press any key to exit...";
        _getch();
        return 1;
    }

    std::wcout << L"[+] Target found: PID " << pid << L"\n";

    // 3. Resolve Full Path of UncapEngine.dll
    std::wstring appDir = GetCurrentExecutableDir();
    std::wstring dllPath = appDir + L"\\UncapEngine.dll";
    std::wstring iniPath = appDir + L"\\UncapEngine.ini";

    // 4. Configure the overlay multiplier (2x the real FPS: 100 -> 200)
    std::wcout << L"[*] Configuring FPS overlay multiplier = 2x...\n";
    UncapSDK::UncapInstaller::WriteConfig(iniPath, 2, true, true);

    // 5. Inject UncapEngine.dll
    std::wcout << L"[*] Injecting UncapEngine.dll into PID " << pid << L"...\n";
    std::wstring err;
    if (!UncapSDK::UncapInstaller::Inject(pid, dllPath, &err)) {
        std::wcout << L"[-] Injection failed: " << err << L"\n";
        std::wcout << L"Press any key to exit...";
        _getch();
        return 1;
    }

    std::wcout << L"[SUCCESS] Uncap Engine successfully attached & patched!\n";
    std::wcout << L"Check your emulator screen: in-game FPS counter should now show 2x the real FPS.\n\n";
    std::wcout << L"Press [Q] or [ESC] in this console to eject & quit.\n";
    std::wcout << L"----------------------------------------------------\n";

    // 6. Real-Time Telemetry Monitor Loop
    while (true) {
        if (_kbhit()) {
            int key = _getch();
            if (key == 'q' || key == 'Q' || key == 27) break;
        }

        auto tel = UncapSDK::UncapInstaller::ReadTelemetry(pid);
        if (tel.valid) {
            std::wcout << L"\r[TELEMETRY] FPS: " << (int)tel.currentFps
                       << L" | AVG: " << tel.avgMs << L" ms"
                       << L" | P95: " << tel.p95Ms << L" ms"
                       << L" | Uncapped: " << (tel.isUncapped ? L"YES" : L"NO")
                       << L" | FakePatch: " << (tel.isFakeActive ? L"ACTIVE" : L"STANDBY")
                       << L"   " << std::flush;
        } else {
            std::wcout << L"\r[TELEMETRY] Waiting for frame pacing signals from emulator...   " << std::flush;
        }

        std::this_thread::sleep_for(std::chrono::milliseconds(250));
    }

    // 7. Clean Eject & Restoration
    std::wcout << L"\n\n[*] Ejecting UncapEngine from PID " << pid << L"...\n";
    UncapSDK::UncapInstaller::Eject(pid, dllPath);
    std::wcout << L"[+] Restored original game states. Bye!\n";
    return 0;
}
