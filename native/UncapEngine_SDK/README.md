# UNCAP ENGINE SDK (Standalone Source & Installer)

SDK ini adalah modul **Uncap Engine & Fake FPS Patcher** yang diekstrak menjadi source code mandiri, bersih, dan modular agar dapat langsung diintegrasikan ke proyek C++ lain (injector, optimizer, internal tool, launcher, dll).

---

## 📁 Struktur Direktori SDK

```
D:\INJECTOR\ECLYPSA\UncapEngine_SDK\
├── SharedContract/
│   └── UncapTelemetry.h        # Struktur memori bersama (Local\OptimaizerFps_<pid>)
├── UncapEngine_DLL/
│   ├── UncapEngine.cpp         # Source code lengkap DLL Uncap (DXGI, OpenGL, Vulkan, Fake FPS)
│   ├── UncapEngine.ini         # Konfigurasi overlay FPS (Multiplier, Jitter, Enable)
│   └── build_dll.bat           # Script 1-klik build UncapEngine.dll
├── UncapInstaller/
│   ├── UncapInstaller.h        # Header loader/installer untuk proyek client
│   └── UncapInstaller.cpp      # Implementasi inject, eject, telemetry reader, privilege
├── Example_Main.cpp            # Contoh lengkap pemanggilan dari aplikasi konsol
├── build_sdk.bat               # Script 1-klik build DLL + Contoh Aplikasi
└── payload/                        # Output binary hasil build (UncapEngine.dll, Example_Main.exe)
```

---

## 🚀 Cara Menggunakan di Proyek Lain

### Langkah 1: Copy File ke Proyek Kamu
Cukup copy 3 file berikut ke proyek C++ kamu:
1. `SharedContract/UncapTelemetry.h`
2. `UncapInstaller/UncapInstaller.h`
3. `UncapInstaller/UncapInstaller.cpp`
4. Letakkan file `UncapEngine.dll` dan `UncapEngine.ini` di sebelah file `.exe` proyek kamu.

### Langkah 2: Panggil API di Kode C++ Kamu

```cpp
#include "UncapInstaller/UncapInstaller.h"

// 1. Berikan privilege debug (agar bisa akses proses emulator)
UncapSDK::UncapInstaller::EnableDebugPrivilege();

// 2. Cari target proses emulator (misal: HD-Player.exe)
DWORD pid = UncapSDK::UncapInstaller::FindProcessId(L"HD-Player.exe");

// 3. Atur multiplier overlay (contoh: 2x FPS asli, jadi 100 FPS tampil 200)
UncapSDK::UncapInstaller::WriteConfig(L"UncapEngine.ini", 2, true, true);

// 4. Inject DLL ke emulator
std::wstring errorMsg;
bool ok = UncapSDK::UncapInstaller::Inject(pid, L"UncapEngine.dll", &errorMsg);
if (ok) {
    // Berhasil di-inject! Counter FPS BlueStacks akan menampilkan 2x FPS asli
}

// 5. Baca telemetry FPS & Frame Time real-time (opsional, untuk UI kamu)
UncapSDK::TelemetryData tel = UncapSDK::UncapInstaller::ReadTelemetry(pid);
if (tel.valid) {
    double currentFps = tel.currentFps; // mis. 200.0 untuk FPS asli 100
    double avgFrameMs = tel.avgMs;      // ~5.00 ms
}

// 6. Eject / Unhook bersih saat keluar aplikasi
UncapSDK::UncapInstaller::Eject(pid, L"UncapEngine.dll");
```

---

## ⚙️ Fitur Inti di dalam UncapEngine.dll

1. **DXGI SwapChain Present Hook (DirectX 11 / 12)**:
   * Mem-patch VTable Present (Slot 8) dan Present1 (Slot 22).
   * Memaksa `SyncInterval = 0` dan mengaktifkan flag `DXGI_PRESENT_ALLOW_TEARING` untuk menembus lock V-Sync layar (60Hz / 144Hz / 240Hz).
2. **OpenGL / EGL / WGL Hooks**:
   * Melakukan inline trampoline hook pada `wglSwapBuffers` dan `eglSwapBuffers`.
   * Menyetel `wglSwapIntervalEXT(0)` dan `eglSwapInterval(0)`.
3. **Vulkan Hook**:
   * Menangkap eksekusi `vkQueuePresentKHR`.
4. **Native BlueStacks In-Game FPS Patcher**:
   * Memindai modul grafis BlueStacks (`HD-Opengl-Native.dll` / `libOpenglRender.dll`) menggunakan signature unik:
     `F2 44 0F 2C C0 44 89 05 ?? ?? ?? ?? 0F 84`
   * Membaca counter FPS asli yang ditulis BlueStacks selama 2 detik, lalu menimpa instruksi writer-nya dengan `Multiplier x FPS asli` (default 2x, maksimal 999). Contoh: 100 FPS asli $\rightarrow$ `200 FPS`.
5. **Dynamic Micro-Jitter**:
   * Nilai FPS bergerak natural (misal: 198 - 202 di sekitar 200) agar tidak terlihat kaku/mati.
6. **Shared Memory Telemetry Provider**:
   * Menulis statistik performa setiap frame ke `Local\OptimaizerFps_<pid>` agar UI aplikasi client bisa menampilkan grafik / angka FPS tanpa lag.

---

## 🛠️ Cara Build Ulang

Buka folder SDK dan jalankan salah satu:
* **`build_sdk.bat`** $\rightarrow$ Otomatis meng-compile `UncapEngine.dll` sekaligus `Example_Main.exe`.
* **`UncapEngine_DLL\build_dll.bat`** $\rightarrow$ Meng-compile khusus file DLL saja.
*(Menggunakan compiler MSVC Visual Studio 2022 `cl.exe` dengan opsi `/std:c++20 /O2 /MT`).*
