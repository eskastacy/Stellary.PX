// ============================================================================
// UncapEngine.cpp
// Standalone In-Process Frame Pacer, V-Sync Bypass, & Native BlueStacks FPS Patcher
//
// Capabilities:
//  1. DXGI SwapChain Hook: Slot 8 (Present) and Slot 22 (Present1).
//     Forces SyncInterval = 0 and DXGI_PRESENT_ALLOW_TEARING.
//  2. OpenGL Hook: wglSwapBuffers & eglSwapBuffers (wglSwapIntervalEXT(0)).
//  3. Vulkan Hook: vkQueuePresentKHR.
//  4. Native BlueStacks FPS Overlay Patcher:
//     Auto-scans OpenGL renderers (HD-Opengl-Native.dll / libOpenglRender.dll)
//     for the internal integer FPS writer. Replaces it with a jump to a code
//     cave that multiplies r8d by the configured factor, stores that value, and
//     jumps back — the overlay always shows the current real FPS times the
//     multiplier and moves with the emulator. The flags are preserved for the
//     conditional jump that follows the store.
//  5. Live Overlay: the multiplication happens in the emulator's own update
//     path, so the displayed value rises and falls with the real frame rate.
//  6. Shared Memory Telemetry: Publishes real-time metrics to Local\OptimaizerFps_<pid>.
//  7. High-Precision Timer: Requests 0.5 ms resolution via NtSetTimerResolution.
// ============================================================================

#include "../SharedContract/UncapTelemetry.h"

#include <windows.h>
#include <tlhelp32.h>
#include <d3d11.h>
#include <dxgi1_2.h>

#include <algorithm>
#include <atomic>
#include <cmath>
#include <cstdarg>
#include <cstdio>
#include <cstring>
#include <cwchar>
#include <string>

#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "dxgi.lib")
#pragma comment(lib, "dxguid.lib")
#pragma comment(lib, "user32.lib")
#pragma comment(lib, "gdi32.lib")

namespace {

void PublishMeta();
bool Readable(const void* addr, size_t bytes);
bool HookSwapPaths();
bool HookVtables();

// ------------------------------------------------------------------ Logging ---
HMODULE g_self = nullptr;
CRITICAL_SECTION g_logLock;
bool g_logReady = false;

void LogLine(const char* fmt, ...) {
    if (!g_logReady) return;

    wchar_t path[MAX_PATH]{};
    ::GetModuleFileNameW(g_self, path, MAX_PATH);
    wchar_t* slash = wcsrchr(path, L'\\');
    if (slash) *(slash + 1) = 0;
    wcsncat(path, L"UncapEngine.log", MAX_PATH - wcslen(path) - 1);

    char body[1024]{};
    va_list ap;
    va_start(ap, fmt);
    std::vsnprintf(body, sizeof(body), fmt, ap);
    va_end(ap);

    SYSTEMTIME st{};
    ::GetLocalTime(&st);

    ::EnterCriticalSection(&g_logLock);
    if (FILE* f = _wfopen(path, L"a")) {
        std::fprintf(f, "%02d:%02d:%02d.%03d  %s\n", st.wHour, st.wMinute,
                     st.wSecond, st.wMilliseconds, body);
        std::fclose(f);
    }
    ::LeaveCriticalSection(&g_logLock);
}

// -------------------------------------------------------------------- State ---
using PresentFn  = HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
using Present1Fn = HRESULT (STDMETHODCALLTYPE*)(IDXGISwapChain1*, UINT, UINT, const DXGI_PRESENT_PARAMETERS*);

PresentFn  g_origPresent  = nullptr;
Present1Fn g_origPresent1 = nullptr;
void**     g_vtSwap       = nullptr;
void**     g_vtSwap1      = nullptr;

HANDLE                g_map    = nullptr;
uncap::SharedMemory*  g_shared = nullptr;

LARGE_INTEGER         g_qpcFreq{};
float                 g_ring[uncap::kSamples]{};
std::atomic<uint32_t> g_head{ 0 };
std::atomic<uint32_t> g_count{ 0 };
std::atomic<uint64_t> g_frames{ 0 };
std::atomic<int64_t>  g_lastQpc{ 0 };
std::atomic<uint64_t> g_dropped{ 0 };
std::atomic<uint32_t> g_flags{ 0 };

std::atomic<bool>     g_disarmed{ false };
std::atomic<bool>     g_rearmRequested{ false };
std::atomic<int>      g_tearing{ -1 };   // -1 unknown, 0 refused, 1 accepted

// --------------------------------------------- BlueStacks Fake FPS Patcher ---
std::atomic<bool>     g_fakeEnabled{ true };
std::atomic<int>      g_multiplier{ 2 };
std::atomic<bool>     g_fakeJitter{ true };

// Overlay: the store BlueStacks uses to publish its FPS is replaced with a jump
// to a cave. The cave keeps the original store (so the real counter stays
// readable), multiplies r8d by the configured factor in place, and jumps back.
// The displayed value is therefore derived from the frame rate BlueStacks just
// measured, every time it measures it — it rises and falls with the emulator
// instead of freezing on one baseline.
std::atomic<int32_t*> g_fpsSlot{ nullptr };
std::atomic<uint8_t*> g_patchAddr{ nullptr };
std::atomic<uint8_t*> g_cave{ nullptr };
std::atomic<int>      g_displayFps{ 0 };
std::atomic<int>      g_realFps{ 0 };
std::atomic<bool>     g_repatchRequested{ false };
uint8_t               g_origPatchBytes[7]{};
std::atomic<bool>     g_patchActive{ false };
std::atomic<bool>     g_fakeStop{ false };

// Live code is patched only while every other thread in the process is
// suspended. Both the FPS store and the inline hook prologues are rewritten
// this way; without it a render thread can fetch a half-written instruction.
CRITICAL_SECTION      g_patchLock;
bool                  g_patchLockReady = false;

HANDLE                g_writerThread = nullptr;

int64_t g_lastLocate      = 0;  // rate-limits the module scan
int64_t g_lastPatchTry    = 0;  // rate-limits failed patch attempts

int DisplayFps() { return g_displayFps.load(std::memory_order_relaxed); }

// Finds `mov [rip+disp], r8d` right after `cvttsd2si r8d, xmm0` — that is how
// every BlueStacks renderer publishes its on-screen FPS. Records the store and
// the int32 it writes. Nothing is modified here.
bool LocateFpsSlot() {
    if (g_patchAddr.load(std::memory_order_relaxed)) return true;

    const wchar_t* candidates[] = {
        L"HD-Opengl-Native.dll",
        L"libOpenglRender.dll",
        L"HD-Player.exe"
    };

    for (const auto* name : candidates) {
        HMODULE hMod = ::GetModuleHandleW(name);
        if (!hMod) continue;

        auto* base = reinterpret_cast<uint8_t*>(hMod);
        auto* dos  = reinterpret_cast<PIMAGE_DOS_HEADER>(base);
        if (dos->e_magic != IMAGE_DOS_SIGNATURE) continue;

        auto* nt = reinterpret_cast<PIMAGE_NT_HEADERS>(base + dos->e_lfanew);
        if (nt->Signature != IMAGE_NT_SIGNATURE) continue;

        const WORD numSecs = nt->FileHeader.NumberOfSections;
        auto* secs = IMAGE_FIRST_SECTION(nt);

        for (WORD i = 0; i < numSecs; ++i) {
            if (!(secs[i].Characteristics & IMAGE_SCN_MEM_EXECUTE)) continue;

            uint8_t* secData = base + secs[i].VirtualAddress;
            DWORD secSize = secs[i].Misc.VirtualSize;
            if (secSize == 0) secSize = secs[i].SizeOfRawData;

            for (DWORD off = 0; off + 15 <= secSize; ++off) {
                uint8_t* p = secData + off;
                if (p[0] == 0xF2 && p[1] == 0x44 && p[2] == 0x0F && p[3] == 0x2C && p[4] == 0xC0 &&
                    p[5] == 0x44 && p[6] == 0x89 && p[7] == 0x05 &&
                    p[12] == 0x0F && p[13] == 0x84) {

                    uint8_t* bp = p + 5; // Start of `mov [rip+disp], r8d` (7 bytes)
                    int32_t disp = 0;
                    std::memcpy(&disp, bp + 3, sizeof(disp));
                    auto* slot = reinterpret_cast<int32_t*>(bp + 7 + disp);
                    if (!Readable(slot, sizeof(int32_t))) continue;

                    std::memcpy(g_origPatchBytes, bp, 7);
                    g_patchAddr.store(bp, std::memory_order_relaxed);
                    g_fpsSlot.store(slot, std::memory_order_relaxed);
                    LogLine("fakefps: counter slot %p in module %p (now %d)",
                            slot, hMod, *slot);
                    return true;
                }
            }
        }
    }
    return false;
}

// Every other thread in the process is stopped while live code is rewritten.
// A render thread fetching a half-written prologue is the classic inline-hook
// crash, and the same hazard applies to the 7-byte FPS store replacement.
struct ThreadFreeze {
    HANDLE handles[512]{};
    int count = 0;

    ThreadFreeze() {
        const DWORD pid  = ::GetCurrentProcessId();
        const DWORD self = ::GetCurrentThreadId();
        HANDLE snap = ::CreateToolhelp32Snapshot(TH32CS_SNAPTHREAD, 0);
        if (snap == INVALID_HANDLE_VALUE) return;

        THREADENTRY32 te{};
        te.dwSize = sizeof(te);
        if (::Thread32First(snap, &te)) {
            do {
                if (te.th32OwnerProcessID != pid || te.th32ThreadID == self) continue;
                if (count >= 512) break;
                HANDLE h = ::OpenThread(THREAD_SUSPEND_RESUME, FALSE, te.th32ThreadID);
                if (!h) continue;
                if (::SuspendThread(h) == (DWORD)-1) { ::CloseHandle(h); continue; }
                handles[count++] = h;
            } while (::Thread32Next(snap, &te));
        }
        ::CloseHandle(snap);
    }

    ~ThreadFreeze() {
        for (int i = 0; i < count; ++i) {
            ::ResumeThread(handles[i]);
            ::CloseHandle(handles[i]);
        }
    }

    ThreadFreeze(const ThreadFreeze&) = delete;
    ThreadFreeze& operator=(const ThreadFreeze&) = delete;
};

// Allocates one executable page within +/-2 GB of the patch so both the 5-byte
// jump to the cave and the cave's jump back fit in rel32.
uint8_t* TryAllocCave(uintptr_t addr) {
    return static_cast<uint8_t*>(::VirtualAlloc(reinterpret_cast<void*>(addr), 0x1000,
                                                MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
}

uint8_t* AllocCaveNear(uint8_t* target) {
    SYSTEM_INFO si{};
    ::GetSystemInfo(&si);
    const uintptr_t gran = si.dwAllocationGranularity ? si.dwAllocationGranularity : 0x10000;
    const uintptr_t base = reinterpret_cast<uintptr_t>(target) & ~(gran - 1);
    const uintptr_t reach = 0x7F000000ull; // stays inside rel32 on both jumps

    // VirtualAlloc only accepts allocation-granularity-aligned addresses, so
    // every probe starts from the aligned base and steps by whole granules.
    // Coarse pass first: a free 16 MB-aligned region is usually found in a
    // handful of probes. The fine pass only runs when the neighbourhood is
    // crowded.
    for (uintptr_t step = 0x1000000; step <= reach; step += 0x1000000) {
        if (uint8_t* p = TryAllocCave(base + step)) return p;
        if (base > step) { if (uint8_t* p = TryAllocCave(base - step)) return p; }
    }
    for (uintptr_t step = gran; step < 0x1000000; step += gran) {
        if (uint8_t* p = TryAllocCave(base + step)) return p;
        if (base > step) { if (uint8_t* p = TryAllocCave(base - step)) return p; }
    }
    return nullptr;
}

bool CaveReaches(uint8_t* cave, uint8_t* bp) {
    const int64_t toCave = int64_t(cave) - int64_t(bp + 5);
    const int64_t toBack = int64_t(bp + 7) - int64_t(cave + 26);
    const int64_t lim = 0x7F000000LL;
    return toCave > -lim && toCave < lim && toBack > -lim && toBack < lim;
}

// Cave layout (26 bytes of code). BlueStacks converts the rate into r8d and
// then stores it; the overlay reads r8d, so the cave multiplies r8d itself and
// stores the same multiplied value. Flags are preserved because the
// instruction after the original store is a conditional jump.
//   +0   push rax
//   +1   mov rax, slot              ; absolute address of the FPS counter
//   +11  pushfq
//   +12  imul r8d, r8d, factor      ; the displayed value
//   +16  mov [rax], r8d             ; replaces the original store
//   +19  popfq
//   +20  pop rax
//   +21  jmp rel32                  ; back to the instruction after the store
void WriteCave(uint8_t* cave, uint8_t* back, int32_t* slot, int multiplier) {
    const uint64_t addr = reinterpret_cast<uint64_t>(slot);
    std::memset(cave, 0xCC, 32);
    cave[0]  = 0x50;                                       // push rax
    cave[1]  = 0x48; cave[2] = 0xB8;                       // mov rax, imm64
    std::memcpy(cave + 3, &addr, 8);
    cave[11] = 0x9C;                                       // pushfq
    cave[12] = 0x45; cave[13] = 0x6B; cave[14] = 0xC0;     // imul r8d, r8d, imm8
    cave[15] = uint8_t(multiplier);
    cave[16] = 0x44; cave[17] = 0x89; cave[18] = 0x00;     // mov [rax], r8d
    cave[19] = 0x9D;                                       // popfq
    cave[20] = 0x58;                                       // pop rax
    cave[21] = 0xE9;                                       // jmp rel32
    const int32_t rel = int32_t(int64_t(back) - int64_t(cave + 26));
    std::memcpy(cave + 22, &rel, 4);
    ::FlushInstructionCache(::GetCurrentProcess(), cave, 32);
}

bool ApplyFpsPatch() {
    if (!g_patchLockReady) return false;

    uint8_t* patched = nullptr;
    bool ok = false;
    ::EnterCriticalSection(&g_patchLock);
    if (!g_patchActive.load(std::memory_order_acquire) &&
        !g_disarmed.load(std::memory_order_relaxed)) {
        uint8_t* bp = g_patchAddr.load(std::memory_order_relaxed);
        if (bp && Readable(bp, 7) && std::memcmp(bp, g_origPatchBytes, 7) == 0) {
            uint8_t* cave = g_cave.load(std::memory_order_relaxed);
            if (cave && !CaveReaches(cave, bp)) cave = nullptr;
            if (!cave) {
                cave = AllocCaveNear(bp);
                if (cave) g_cave.store(cave, std::memory_order_relaxed);
            }

            int32_t* slot = g_fpsSlot.load(std::memory_order_relaxed);
            if (cave && slot) {
                const int mult = (std::max)(1, g_multiplier.load(std::memory_order_relaxed));

                uint8_t code[7];
                code[0] = 0xE9;              // jmp cave
                const int32_t rel = int32_t(int64_t(cave) - int64_t(bp + 5));
                std::memcpy(code + 1, &rel, 4);
                code[5] = 0x90;              // NOP padding
                code[6] = 0x90;

                DWORD oldProtect = 0;
                if (::VirtualProtect(bp, 7, PAGE_EXECUTE_READWRITE, &oldProtect)) {
                    {
                        // The cave is rewritten under the same freeze: a thread
                        // suspended inside it must not resume on half-written
                        // instructions.
                        ThreadFreeze freeze;
                        WriteCave(cave, bp + 7, slot, mult);
                        std::memcpy(bp, code, 7);
                    }
                    ::VirtualProtect(bp, 7, oldProtect, &oldProtect);
                    ::FlushInstructionCache(::GetCurrentProcess(), bp, 7);
                    g_patchActive.store(true, std::memory_order_release);
                    g_flags.fetch_or(uncap::kFlagFakeFpsActive, std::memory_order_relaxed);
                    patched = bp;
                    ok = true;
                }
            }
        } else if (bp) {
            // The renderer restarted; the recorded address no longer holds the
            // store. Drop it and let the scanner find the new one.
            g_patchAddr.store(nullptr, std::memory_order_relaxed);
            g_fpsSlot.store(nullptr, std::memory_order_relaxed);
        }
    } else {
        ok = true;
    }
    ::LeaveCriticalSection(&g_patchLock);

    if (patched) LogLine("fakefps: live cave %p patched at %p", (void*)g_cave.load(), patched);
    return ok;
}

bool PatchOverlay() {
    return LocateFpsSlot() && ApplyFpsPatch();
}

void RestoreFakePatch() {
    if (!g_patchLockReady) return;

    bool restored = false;
    bool clearActive = false;
    ::EnterCriticalSection(&g_patchLock);
    if (g_patchActive.load(std::memory_order_acquire)) {
        uint8_t* patch = g_patchAddr.load(std::memory_order_relaxed);
        if (!patch || !Readable(patch, 7)) {
            clearActive = true;
        } else {
            // Restore only when the bytes are still ours: the 5-byte jump must
            // point at our cave. After a renderer restart the address may
            // belong to unrelated code.
            bool ours = patch[0] == 0xE9 && patch[5] == 0x90 && patch[6] == 0x90;
            if (ours) {
                int32_t rel = 0;
                std::memcpy(&rel, patch + 1, sizeof(rel));
                ours = (patch + 5 + rel) == g_cave.load(std::memory_order_relaxed);
            }
            if (ours) {
                DWORD old = 0;
                if (::VirtualProtect(patch, 7, PAGE_EXECUTE_READWRITE, &old)) {
                    {
                        ThreadFreeze freeze;
                        std::memcpy(patch, g_origPatchBytes, 7);
                    }
                    ::VirtualProtect(patch, 7, old, &old);
                    ::FlushInstructionCache(::GetCurrentProcess(), patch, 7);
                    restored = true;
                    clearActive = true;
                }
                // If VirtualProtect failed, keep the active flag so the worker
                // does not mistake the still-patched bytes for the original.
            } else {
                g_patchAddr.store(nullptr, std::memory_order_relaxed);
                g_fpsSlot.store(nullptr, std::memory_order_relaxed);
                clearActive = true;
            }
        }

        if (clearActive) {
            g_patchActive.store(false, std::memory_order_release);
            g_flags.fetch_and(~uncap::kFlagFakeFpsActive, std::memory_order_relaxed);
        }
    }
    ::LeaveCriticalSection(&g_patchLock);

    if (restored) LogLine("fakefps: restored original BlueStacks instructions");
}

void LoadConfig() {
    wchar_t path[MAX_PATH]{};
    if (g_self) ::GetModuleFileNameW(g_self, path, MAX_PATH);
    wchar_t* slash = wcsrchr(path, L'\\');
    if (slash) *(slash + 1) = 0;
    wcsncat(path, L"UncapEngine.ini", MAX_PATH - wcslen(path) - 1);

    const int enabled    = ::GetPrivateProfileIntW(L"FakeFps", L"Enabled", 1, path);
    const int multiplier = ::GetPrivateProfileIntW(L"FakeFps", L"Multiplier", 2, path);
    const int jitter     = ::GetPrivateProfileIntW(L"FakeFps", L"Jitter", 1, path);

    g_fakeEnabled.store(enabled != 0, std::memory_order_relaxed);
    g_multiplier.store((std::max)(1, (std::min)(10, multiplier)), std::memory_order_relaxed);
    g_fakeJitter.store(jitter != 0, std::memory_order_relaxed);

    // The patch is applied by FakeWriter once the counter reports a real rate.
}

// A re-arm restores the original store and starts over. The cave stays
// allocated; the next install rewrites it with the current multiplier.
void ResetOverlayState() {
    if (g_patchLockReady) ::EnterCriticalSection(&g_patchLock);
    g_lastLocate   = 0;
    g_lastPatchTry = 0;
    g_realFps.store(0, std::memory_order_relaxed);
    g_displayFps.store(0, std::memory_order_relaxed);
    g_repatchRequested.store(false, std::memory_order_relaxed);
    if (g_patchLockReady) ::LeaveCriticalSection(&g_patchLock);
}

// The counter holds the real value until the patch is installed, and
// factor x real afterwards (the cave multiplies every store). Dividing by the
// factor recovers the rate the emulator actually measured.
int ReadRealFps() {
    int32_t* slot = g_fpsSlot.load(std::memory_order_relaxed);
    if (!slot || !Readable(slot, sizeof(int32_t))) return 0;
    const int value = (std::min)(999999, (std::max)(0, int(*slot)));
    const int mult = (std::max)(1, g_multiplier.load(std::memory_order_relaxed));
    const int real = g_patchActive.load(std::memory_order_acquire) ? value / mult : value;
    return (std::min)(9999, real);
}

DWORD WINAPI FakeWriter(LPVOID) {
    while (!g_fakeStop.load(std::memory_order_relaxed)) {
        // Re-arm work is queued by the export so the emulator's UI thread is
        // never made to scan modules or install hooks.
        if (g_rearmRequested.exchange(false, std::memory_order_relaxed) &&
            !g_disarmed.load(std::memory_order_relaxed)) {
            HookSwapPaths();
            HookVtables();
            LogLine("re-arm: hooks reinstalled");
        }

        LARGE_INTEGER now{};
        ::QueryPerformanceCounter(&now);

        if (!g_fakeEnabled.load(std::memory_order_relaxed)) {
            ::Sleep(100);
            continue;
        }

        // A multiplier change rewrites the cave; restoring the store first
        // lets the normal install path pick up the new factor.
        if (g_repatchRequested.exchange(false, std::memory_order_relaxed) &&
            g_patchActive.load(std::memory_order_acquire)) {
            RestoreFakePatch();
            g_lastPatchTry = 0;
        }

        const bool armed = !g_disarmed.load(std::memory_order_relaxed);
        const int real = ReadRealFps();
        g_realFps.store(real, std::memory_order_relaxed);

        // A failed locate is retried once a second: the signature scan walks
        // tens of MB and must never turn into a per-frame cost. The cave can be
        // installed before the counter reports a value — it multiplies whatever
        // BlueStacks writes next.
        const double sinceTry = g_qpcFreq.QuadPart > 0
            ? double(now.QuadPart - g_lastPatchTry) / double(g_qpcFreq.QuadPart)
            : 1.0;
        if (armed && !g_patchActive.load(std::memory_order_acquire) &&
            sinceTry >= 1.0) {
            g_lastPatchTry = now.QuadPart;
            PatchOverlay();
        }

        // The cave multiplies in place every time BlueStacks publishes its
        // counter, so the displayed value is always factor x the current rate.
        // The worker only mirrors it for telemetry.
        const int mult = (std::max)(1, g_multiplier.load(std::memory_order_relaxed));
        if (armed && real > 0 && g_patchActive.load(std::memory_order_acquire))
            g_displayFps.store((std::min)(9999, (std::max)(1, real * mult)),
                               std::memory_order_relaxed);
        else
            g_displayFps.store(0, std::memory_order_relaxed);

        // Publish synthetic telemetry to shared memory if uncap is active.
        // No jitter here: the card must show the same number the overlay does.
        const int target = DisplayFps();
        if (g_shared && target > 0 && armed &&
            g_fakeEnabled.load(std::memory_order_relaxed)) {
            double fakeFps = double((std::max)(1, target));
            double fakeAvgMs = 1000.0 / fakeFps;
            g_shared->avgMs = fakeAvgMs;
            g_shared->minMs = fakeAvgMs * 0.96;
            g_shared->p95Ms = fakeAvgMs * 1.04;
            g_shared->maxMs = fakeAvgMs * 1.12;
            PublishMeta();
        }

        ::Sleep(60);
    }
    return 0;
}

// ----------------------------------------------------------------- Telemetry ---
bool WriteSlot(void** vt, size_t index, void* want, void** prev) {
    if (!vt) return false;
    MEMORY_BASIC_INFORMATION mbi{};
    if (!::VirtualQuery(&vt[index], &mbi, sizeof(mbi))) return false;

    DWORD oldProtect = 0;
    if (!::VirtualProtect(mbi.BaseAddress, mbi.RegionSize, PAGE_READWRITE, &oldProtect))
        return false;

    void* current = vt[index];
    if (current != want) {
        if (prev) *prev = current;
        vt[index] = want;
    }
    ::VirtualProtect(mbi.BaseAddress, mbi.RegionSize, oldProtect, &oldProtect);
    ::FlushInstructionCache(::GetCurrentProcess(), &vt[index], sizeof(void*));
    return current != want;
}

void PublishMeta() {
    if (!g_shared) return;
    g_shared->sampleCount = g_count.load(std::memory_order_relaxed);
    g_shared->head        = g_head.load(std::memory_order_relaxed);
    g_shared->frames      = g_frames.load(std::memory_order_relaxed);
    g_shared->dropped     = g_dropped.load(std::memory_order_relaxed);
    g_shared->flags       = g_flags.load(std::memory_order_relaxed);
    g_shared->qpcFreq     = g_qpcFreq.QuadPart;
    g_shared->lastQpc     = g_lastQpc.load(std::memory_order_relaxed);
}

void Record() {
    LARGE_INTEGER now{};
    ::QueryPerformanceCounter(&now);

    const int64_t prev = g_lastQpc.exchange(now.QuadPart, std::memory_order_relaxed);
    g_frames.fetch_add(1, std::memory_order_relaxed);

    double ms = 0.0;
    if (prev != 0 && g_qpcFreq.QuadPart > 0)
        ms = double(now.QuadPart - prev) * 1000.0 / double(g_qpcFreq.QuadPart);

    if (ms <= 0.0 || ms > 500.0) { PublishMeta(); return; }

    const uint32_t slot = g_head.load(std::memory_order_relaxed);
    g_ring[slot % uncap::kSamples] = float(ms);
    g_head.store(slot + 1, std::memory_order_relaxed);
    if (slot + 1 > uncap::kSamples) g_count.store(uncap::kSamples, std::memory_order_relaxed);
    else                            g_count.store(slot + 1, std::memory_order_relaxed);

    if (!g_shared) return;

    const uint32_t n = std::min<uint32_t>(g_count.load(std::memory_order_relaxed), uncap::kSamples);
    float sorted[uncap::kSamples];
    std::memcpy(sorted, g_ring, size_t(n) * sizeof(float));
    std::sort(sorted, sorted + n);

    double sum = 0;
    for (uint32_t i = 0; i < n; ++i) sum += sorted[i];

    const double avg = n ? sum / n : 0.0;
    const double p95 = n ? sorted[size_t(std::min<uint32_t>(n - 1, uint32_t(n * 0.95)))] : 0.0;

    const int display = DisplayFps();
    if (display > 0 && !g_disarmed.load(std::memory_order_relaxed) &&
        g_fakeEnabled.load(std::memory_order_relaxed)) {
        int j = g_fakeJitter.load(std::memory_order_relaxed) ? ((rand() % 7) - 3) : 0;
        double fakeFps = double((std::max)(1, display + j));
        double fakeAvgMs = 1000.0 / fakeFps;
        g_shared->avgMs = fakeAvgMs;
        g_shared->p95Ms = fakeAvgMs * 1.04;
        g_shared->minMs = fakeAvgMs * 0.96;
        g_shared->maxMs = fakeAvgMs * 1.12;
    } else {
        g_shared->avgMs = avg;
        g_shared->p95Ms = p95;
        g_shared->minMs = n ? sorted[0] : 0.0;
        g_shared->maxMs = n ? sorted[n - 1] : 0.0;
    }

    if (avg > 0.0 && ms > avg * 2.0) g_dropped.fetch_add(1, std::memory_order_relaxed);
    PublishMeta();
}

void UpdateSurface(IDXGISwapChain* sc) {
    if (!g_shared) return;
    DXGI_SWAP_CHAIN_DESC d{};
    if (FAILED(sc->GetDesc(&d))) return;
    g_shared->width  = d.BufferDesc.Width;
    g_shared->height = d.BufferDesc.Height;
    g_shared->refreshHz = d.BufferDesc.RefreshRate.Denominator
        ? d.BufferDesc.RefreshRate.Numerator / d.BufferDesc.RefreshRate.Denominator
        : 0;
}

// ------------------------------------------------------- DXGI Present Hooks ---
HRESULT STDMETHODCALLTYPE HookPresent(IDXGISwapChain* sc, UINT sync, UINT flags) {
    if (!g_origPresent) return E_FAIL;
    if (g_shared) g_shared->syncInterval = int32_t(sync);

    UINT outSync  = 0;
    UINT outFlags = flags;

    if (sync != 0) {
        const int tearing = g_tearing.load(std::memory_order_relaxed);
        if (tearing != 0) outFlags |= DXGI_PRESENT_ALLOW_TEARING;
        if (g_shared) g_shared->forcedSync = 0;
        g_flags.fetch_or(uncap::kFlagUncapped, std::memory_order_relaxed);
    } else if (g_shared) {
        g_shared->forcedSync = 0;
    }

    HRESULT hr = g_origPresent(sc, outSync, outFlags);
    if (hr == DXGI_ERROR_INVALID_CALL && (outFlags & DXGI_PRESENT_ALLOW_TEARING)) {
        g_tearing.store(0, std::memory_order_relaxed);
        g_flags.fetch_or(uncap::kFlagTearingRefused, std::memory_order_relaxed);
        g_flags.fetch_and(~uncap::kFlagTearingAllowed, std::memory_order_relaxed);
        outFlags &= ~DXGI_PRESENT_ALLOW_TEARING;
        hr = g_origPresent(sc, outSync, outFlags);
    } else if (SUCCEEDED(hr) && (outFlags & DXGI_PRESENT_ALLOW_TEARING)) {
        if (g_tearing.exchange(1, std::memory_order_relaxed) != 1) {
            g_flags.fetch_or(uncap::kFlagTearingAllowed, std::memory_order_relaxed);
            LogLine("present: ALLOW_TEARING accepted, sync forced to 0");
        }
    }
    Record();
    if (g_shared && (g_frames.load(std::memory_order_relaxed) & 0x7F) == 0)
        UpdateSurface(sc);
    return hr;
}

HRESULT STDMETHODCALLTYPE HookPresent1(IDXGISwapChain1* sc, UINT sync, UINT flags, const DXGI_PRESENT_PARAMETERS* params) {
    if (!g_origPresent1) return E_FAIL;
    if (g_shared) g_shared->syncInterval = int32_t(sync);

    UINT outSync  = 0;
    UINT outFlags = flags;
    if (sync != 0) {
        if (g_tearing.load(std::memory_order_relaxed) != 0) outFlags |= DXGI_PRESENT_ALLOW_TEARING;
        if (g_shared) g_shared->forcedSync = 0;
        g_flags.fetch_or(uncap::kFlagUncapped, std::memory_order_relaxed);
    }

    HRESULT hr = g_origPresent1(sc, outSync, outFlags, params);
    if (hr == DXGI_ERROR_INVALID_CALL && (outFlags & DXGI_PRESENT_ALLOW_TEARING)) {
        g_tearing.store(0, std::memory_order_relaxed);
        g_flags.fetch_or(uncap::kFlagTearingRefused, std::memory_order_relaxed);
        outFlags &= ~DXGI_PRESENT_ALLOW_TEARING;
        hr = g_origPresent1(sc, outSync, outFlags, params);
    }
    Record();
    return hr;
}

// ---------------------------------------------------- Inline Trampoline Hook ---
struct HookRec {
    uint8_t* target   = nullptr;
    uint8_t* tramp    = nullptr;
    uint8_t  orig[64]{};
    size_t   moved    = 0;
    bool     active   = false;
};

HookRec g_eglHook, g_vkHook, g_wglHook;

constexpr size_t kPatchLen = 12;

// Minimal x86-64 decoder for the prologue copy. It only needs exact lengths
// and the two properties we cannot relocate: RIP-relative memory operands and
// relative branches. Anything it does not understand fails the hook instead of
// producing a broken trampoline.
struct Insn {
    size_t len = 0;
    bool ripRelative = false;
    bool relativeBranch = false;
};

bool DecodeInsn(const uint8_t* p, Insn& out) {
    size_t i = 0;
    bool opsize = false;
    int rexW = 0;

    for (int guard = 0; guard < 8; ++guard) {
        const uint8_t b = p[i];
        if (b == 0x66) { opsize = true; ++i; continue; }
        if (b == 0x67 || b == 0xF0 || b == 0xF2 || b == 0xF3 ||
            b == 0x2E || b == 0x36 || b == 0x3E || b == 0x26 ||
            b == 0x64 || b == 0x65) { ++i; continue; }
        break;
    }
    if ((p[i] & 0xF0) == 0x40) { rexW = (p[i] & 0x08) ? 1 : 0; ++i; }
    const uint8_t op = p[i];

    auto hasModRm = [](uint8_t o) {
        if (o <= 0x3F) return (o & 0x07) < 0x04;
        switch (o) {
            case 0x62: case 0x63: case 0x69: case 0x6B: case 0xC0: case 0xC1:
            case 0x80: case 0x81: case 0x82: case 0x83: case 0xC6: case 0xC7:
            case 0xD0: case 0xD1: case 0xD2: case 0xD3: case 0xFE: case 0xFF:
            case 0x84: case 0x85: case 0x86: case 0x87: case 0x88: case 0x89:
            case 0x8A: case 0x8B: case 0x8D: case 0x8F: case 0xF6: case 0xF7:
                return true;
            default: return false;
        }
    };

    if (op == 0xE8 || op == 0xE9) { out = { i + 5, false, true }; return true; }
    if (op == 0xEB || (op >= 0x70 && op <= 0x7F) ||
        (op >= 0xE0 && op <= 0xE3)) { out = { i + 2, false, true }; return true; }
    if (op == 0x90 || op == 0xCC || op == 0xC3 || (op >= 0x50 && op <= 0x5F))
        { out = { i + 1, false, false }; return true; }
    if (op >= 0xB0 && op <= 0xB7) { out = { i + 2, false, false }; return true; }
    if (op == 0x6A || op == 0xA8) { out = { i + 2, false, false }; return true; }
    if (op == 0x68) { out = { i + 1 + (opsize ? 2u : 4u), false, false }; return true; }
    if (op >= 0xB8 && op <= 0xBF) { out = { i + 1 + (rexW ? 8u : (opsize ? 2u : 4u)), false, false }; return true; }
    if (op >= 0xA0 && op <= 0xA3) return false; // moffs: not worth decoding

    if (!hasModRm(op)) return false;

    const uint8_t m = p[i + 1];
    const uint8_t mod = uint8_t(m >> 6);
    const uint8_t rm  = uint8_t(m & 0x07);
    size_t n = 1; // ModRM itself
    if (mod != 3) {
        if (rm == 4) {
            ++n; // SIB
            const uint8_t sib = p[i + 2];
            if (mod == 0 && (sib & 0x07) == 5) n += 4;
        } else if (mod == 0 && rm == 5) {
            out.ripRelative = true;
            n += 4;
        }
        if (mod == 1) n += 1;
        if (mod == 2) n += 4;
    }

    size_t imm = 0;
    switch (op) {
        case 0x69: case 0x81: case 0xC7: imm = opsize ? 2u : 4u; break;
        case 0x6B: case 0x80: case 0x82: case 0x83: case 0xC0: case 0xC1: case 0xC6:
            imm = 1; break;
        case 0xF6: imm = ((m >> 3) & 7) <= 1 ? 1u : 0u; break;
        case 0xF7: imm = ((m >> 3) & 7) <= 1 ? (opsize ? 2u : 4u) : 0u; break;
        case 0xC2: case 0xCA: imm = 2; break;
        case 0xCD: imm = 1; break;
        default: imm = 0; break;
    }
    if (op == 0x04 || op == 0x0C || op == 0x14 || op == 0x1C ||
        op == 0x24 || op == 0x2C || op == 0x34 || op == 0x3C) imm = 1;
    if (op == 0x05 || op == 0x0D || op == 0x15 || op == 0x1D ||
        op == 0x25 || op == 0x2D || op == 0x35 || op == 0x3D) imm = opsize ? 2u : 4u;

    out.len = i + 1 + n + imm;
    out.relativeBranch = false;
    return true;
}

void EmitAbsJmp(uint8_t* at, const void* dest) {
    at[0] = 0x48; at[1] = 0xB8;
    *reinterpret_cast<uint64_t*>(at + 2) = reinterpret_cast<uint64_t>(dest);
    at[10] = 0xFF; at[11] = 0xE0;
}

bool BuildTrampoline(uint8_t* target, HookRec& rec) {
    size_t need = 0;
    while (need < kPatchLen) {
        Insn ins;
        if (!DecodeInsn(target + need, ins)) return false;
        if (ins.relativeBranch || ins.ripRelative) return false;
        if (ins.len == 0 || need + ins.len > 48) return false;
        need += ins.len;
    }

    auto* tramp = static_cast<uint8_t*>(::VirtualAlloc(nullptr, 512, MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE));
    if (!tramp) return false;

    std::memcpy(tramp, target, need);
    EmitAbsJmp(tramp + need, target + need);
    std::memcpy(rec.orig, target, need);
    rec.target = target;
    rec.tramp  = tramp;
    rec.moved  = need;
    return true;
}

bool InstallHook(HookRec& rec, void* detour) {
    if (rec.active || !rec.target) return rec.active;
    if (!BuildTrampoline(rec.target, rec)) return false;

    uint8_t patch[64];
    std::memset(patch, 0x90, rec.moved);
    EmitAbsJmp(patch, detour);

    DWORD oldProtect = 0;
    if (!::VirtualProtect(rec.target, rec.moved, PAGE_EXECUTE_READWRITE, &oldProtect)) {
        ::VirtualFree(rec.tramp, 0, MEM_RELEASE);
        rec.tramp = nullptr;
        return false;
    }
    {
        ThreadFreeze freeze;
        std::memcpy(rec.target, patch, rec.moved);
    }
    ::VirtualProtect(rec.target, rec.moved, oldProtect, &oldProtect);
    ::FlushInstructionCache(::GetCurrentProcess(), rec.target, rec.moved);
    rec.active = true;
    return true;
}

void RemoveHook(HookRec& rec) {
    if (!rec.active || !rec.target) return;
    DWORD oldProtect = 0;
    if (!::VirtualProtect(rec.target, rec.moved, PAGE_EXECUTE_READWRITE, &oldProtect)) {
        LogLine("unhook: VirtualProtect failed at %p", rec.target);
        return;
    }
    {
        ThreadFreeze freeze;
        std::memcpy(rec.target, rec.orig, rec.moved);
    }
    ::VirtualProtect(rec.target, rec.moved, oldProtect, &oldProtect);
    ::FlushInstructionCache(::GetCurrentProcess(), rec.target, rec.moved);
    // The trampoline is deliberately not freed. A thread that was suspended
    // inside it resumes there after the original prologue is back; releasing
    // the page underneath it would crash the process.
    rec.active = false;
}

// ---------------------------------------------------- OpenGL / Vulkan Detours ---
using EglSwapBuffersFn  = unsigned (__stdcall*)(void*, void*);
using EglSwapIntervalFn = int (__stdcall*)(void*, int);
using WglSwapBuffersFn  = int (__stdcall*)(HDC);
using WglSwapIntervalFn = int (__stdcall*)(int);
using VkQueuePresentFn  = int (__stdcall*)(void*, void*);

EglSwapIntervalFn g_eglSwapInterval = nullptr;
WglSwapIntervalFn g_wglSwapInterval = nullptr;
std::atomic<bool> g_intervalSet{ false };

unsigned __stdcall HookEglSwapBuffers(void* dpy, void* surface) {
    if (!g_intervalSet.load(std::memory_order_relaxed) && g_eglSwapInterval) {
        if (g_eglSwapInterval(dpy, 0) == 1) {
            g_intervalSet.store(true, std::memory_order_relaxed);
            g_flags.fetch_or(uncap::kFlagUncapped, std::memory_order_relaxed);
            if (g_shared) g_shared->forcedSync = 0;
        }
    }
    const unsigned ok = reinterpret_cast<EglSwapBuffersFn>(g_eglHook.tramp)(dpy, surface);
    Record();
    return ok;
}

int __stdcall HookWglSwapBuffers(HDC dc) {
    if (!g_intervalSet.load(std::memory_order_relaxed) && !g_wglSwapInterval) {
        using GetProcFn = void* (__stdcall*)(const char*);
        auto* getPb = reinterpret_cast<GetProcFn>(::GetProcAddress(::GetModuleHandleW(L"opengl32.dll"), "wglGetProcAddress"));
        if (getPb) g_wglSwapInterval = reinterpret_cast<WglSwapIntervalFn>(getPb("wglSwapIntervalEXT"));
        if (g_wglSwapInterval) {
            g_wglSwapInterval(0);
            g_intervalSet.store(true, std::memory_order_relaxed);
            g_flags.fetch_or(uncap::kFlagUncapped, std::memory_order_relaxed);
            if (g_shared) g_shared->forcedSync = 0;
        }
    }
    const int ok = reinterpret_cast<WglSwapBuffersFn>(g_wglHook.tramp)(dc);
    Record();
    return ok;
}

int __stdcall HookVkQueuePresent(void* queue, void* presentInfo) {
    const int ok = reinterpret_cast<VkQueuePresentFn>(g_vkHook.tramp)(queue, presentInfo);
    Record();
    return ok;
}

bool Readable(const void* addr, size_t bytes) {
    MEMORY_BASIC_INFORMATION mbi{};
    if (!::VirtualQuery(addr, &mbi, sizeof(mbi))) return false;
    if (mbi.State != MEM_COMMIT) return false;
    const DWORD guard = PAGE_GUARD | PAGE_NOACCESS;
    if (mbi.Protect & guard) return false;
    auto* end = static_cast<const uint8_t*>(addr) + bytes;
    auto* regionEnd = static_cast<const uint8_t*>(mbi.BaseAddress) + mbi.RegionSize;
    return end <= regionEnd;
}

// Export-forwarding unhook: opengl32/vulkan-1 often export a 6-byte
// `jmp qword ptr [rip+disp]` thunk (FF 25) or a 5-byte `jmp rel32`. Hooking the
// thunk copies a relative/indirect jump into the trampoline, so follow the
// chain to the real implementation first.
void* ResolveThunk(void* target) {
    auto* p = static_cast<uint8_t*>(target);
    for (int i = 0; i < 4 && p; ++i) {
        if (!Readable(p, 16)) return p;
        if (p[0] == 0xFF && p[1] == 0x25) {
            int32_t disp = 0;
            std::memcpy(&disp, p + 2, sizeof(disp));
            auto* slot = reinterpret_cast<uint8_t**>(p + 6 + disp);
            if (!Readable(slot, sizeof(void*))) return p;
            void* dst = *slot;
            if (!dst || dst == p) return p;
            p = static_cast<uint8_t*>(dst);
            continue;
        }
        if (p[0] == 0xE9) {
            int32_t rel = 0;
            std::memcpy(&rel, p + 1, sizeof(rel));
            p = p + 5 + rel;
            continue;
        }
        break;
    }
    return p;
}

bool HookExport(const wchar_t* module, const char* fn, HookRec& rec, void* detour) {
    HMODULE mod = ::GetModuleHandleW(module);
    if (!mod) return false;
    void* target = reinterpret_cast<void*>(::GetProcAddress(mod, fn));
    if (!target) return false;
    rec.target = static_cast<uint8_t*>(ResolveThunk(target));
    return InstallHook(rec, detour);
}

bool HookSwapPaths() {
    // Eject can land between the worker's re-arm check and this call; the
    // guard here is the last word, so a disarmed engine never re-hooks.
    if (g_disarmed.load(std::memory_order_relaxed)) return false;
    bool any = false;
    if (!g_eglHook.active && HookExport(L"libEGL.dll", "eglSwapBuffers", g_eglHook, reinterpret_cast<void*>(&HookEglSwapBuffers))) {
        if (HMODULE egl = ::GetModuleHandleW(L"libEGL.dll"))
            g_eglSwapInterval = reinterpret_cast<EglSwapIntervalFn>(::GetProcAddress(egl, "eglSwapInterval"));
        g_flags.fetch_or(uncap::kFlagGlHooked, std::memory_order_relaxed);
        any = true;
    }
    if (!g_vkHook.active && HookExport(L"vulkan-1.dll", "vkQueuePresentKHR", g_vkHook, reinterpret_cast<void*>(&HookVkQueuePresent))) {
        g_flags.fetch_or(uncap::kFlagVkHooked, std::memory_order_relaxed);
        any = true;
    }
    if (!g_wglHook.active && HookExport(L"opengl32.dll", "wglSwapBuffers", g_wglHook, reinterpret_cast<void*>(&HookWglSwapBuffers))) {
        g_flags.fetch_or(uncap::kFlagGlHooked, std::memory_order_relaxed);
        any = true;
    }
    return any;
}

// ----------------------------------------------------------- D3D11 Probe Init ---
HWND DummyWindow() {
    WNDCLASSEXW wc{};
    wc.cbSize = sizeof(wc);
    wc.lpfnWndProc = ::DefWindowProcW;
    wc.hInstance = g_self;
    wc.lpszClassName = L"UncapProbeWnd";
    ::RegisterClassExW(&wc);
    return ::CreateWindowExW(0, L"UncapProbeWnd", L"", WS_POPUP, 0, 0, 16, 16, nullptr, nullptr, g_self, nullptr);
}

bool HookVtables() {
    if (g_disarmed.load(std::memory_order_relaxed)) return false;
    HWND hwnd = DummyWindow();
    if (!hwnd) return false;

    DXGI_SWAP_CHAIN_DESC sd{};
    sd.BufferCount = 1;
    sd.BufferDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
    sd.BufferDesc.Width = 16;
    sd.BufferDesc.Height = 16;
    sd.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;
    sd.OutputWindow = hwnd;
    sd.SampleDesc.Count = 1;
    sd.Windowed = TRUE;
    sd.SwapEffect = DXGI_SWAP_EFFECT_DISCARD;

    D3D_FEATURE_LEVEL levels[] = { D3D_FEATURE_LEVEL_11_0, D3D_FEATURE_LEVEL_10_0 };
    IDXGISwapChain*      sc  = nullptr;
    ID3D11Device*        dev = nullptr;
    ID3D11DeviceContext* ctx = nullptr;
    D3D_FEATURE_LEVEL    got = D3D_FEATURE_LEVEL_11_0;

    HRESULT hr = ::D3D11CreateDeviceAndSwapChain(nullptr, D3D_DRIVER_TYPE_HARDWARE, nullptr, 0, levels, 2,
                                                 D3D11_SDK_VERSION, &sd, &sc, &dev, &got, &ctx);
    if (FAILED(hr) || !sc) {
        ::DestroyWindow(hwnd);
        return false;
    }

    // Write the slots on every call: Unhook restores the originals, so a re-arm
    // must re-install both detours rather than skip because an original exists.
    g_vtSwap = *(void***)sc;
    bool any = false;
    {
        void* prev = nullptr;
        const bool changed = WriteSlot(g_vtSwap, 8, (void*)&HookPresent, &prev);
        if (changed || g_vtSwap[8] == (void*)&HookPresent) {
            if (!g_origPresent) g_origPresent = reinterpret_cast<PresentFn>(prev);
            g_flags.fetch_or(uncap::kFlagPresentHooked, std::memory_order_relaxed);
            any = true;
        }
    }

    IDXGISwapChain1* sc1 = nullptr;
    if (SUCCEEDED(sc->QueryInterface(IID_IDXGISwapChain1, (void**)&sc1)) && sc1) {
        void** vt1 = *(void***)sc1;
        if (vt1 != g_vtSwap) {
            void* prev = nullptr;
            const bool changed = WriteSlot(vt1, 8, (void*)&HookPresent, &prev);
            if (changed || vt1[8] == (void*)&HookPresent) {
                if (!g_origPresent) g_origPresent = reinterpret_cast<PresentFn>(prev);
                g_flags.fetch_or(uncap::kFlagPresentHooked, std::memory_order_relaxed);
                any = true;
            }

            void* prev1 = nullptr;
            const bool changed1 = WriteSlot(vt1, 22, (void*)&HookPresent1, &prev1);
            if (changed1 || vt1[22] == (void*)&HookPresent1) {
                if (!g_origPresent1) g_origPresent1 = reinterpret_cast<Present1Fn>(prev1);
                g_vtSwap1 = vt1;
                g_flags.fetch_or(uncap::kFlagPresent1Hooked, std::memory_order_relaxed);
                any = true;
            }
        }
        sc1->Release();
    }

    if (ctx) ctx->Release();
    sc->Release();
    dev->Release();
    ::DestroyWindow(hwnd);
    return any;
}

void Unhook() {
    if (g_vtSwap && g_origPresent) {
        void* prev = nullptr;
        WriteSlot(g_vtSwap, 8, (void*)g_origPresent, &prev);
    }
    if (g_vtSwap1) {
        void* prev = nullptr;
        if (g_origPresent)  WriteSlot(g_vtSwap1, 8,  (void*)g_origPresent,  &prev);
        if (g_origPresent1) WriteSlot(g_vtSwap1, 22, (void*)g_origPresent1, &prev);
    }
    RemoveHook(g_eglHook);
    RemoveHook(g_vkHook);
    RemoveHook(g_wglHook);
}

void RaiseTimerResolution() {
    using SetResFn = LONG (NTAPI*)(ULONG, BOOLEAN, PULONG);
    HMODULE nt = ::GetModuleHandleW(L"ntdll.dll");
    if (!nt) return;
    auto setRes = reinterpret_cast<SetResFn>(::GetProcAddress(nt, "NtSetTimerResolution"));
    if (!setRes) return;

    ULONG want = 5000; // 0.5 ms
    ULONG cur  = 0;
    if (setRes(want, TRUE, &cur) == 0) {
        g_flags.fetch_or(uncap::kFlagTimerSet, std::memory_order_relaxed);
        if (g_shared) g_shared->timerMs = double(cur) / 10000.0;
    }
}

bool OpenShared() {
    char name[64]{};
    std::snprintf(name, sizeof(name), "Local\\OptimaizerFps_%lu", (unsigned long)::GetCurrentProcessId());

    g_map = ::CreateFileMappingA(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, sizeof(uncap::SharedMemory), name);
    if (!g_map) return false;

    g_shared = (uncap::SharedMemory*)::MapViewOfFile(g_map, FILE_MAP_ALL_ACCESS, 0, 0, sizeof(uncap::SharedMemory));
    if (!g_shared) return false;

    if (::GetLastError() != ERROR_ALREADY_EXISTS) {
        *g_shared = uncap::SharedMemory{};
    }

    std::memcpy(g_shared->magic, uncap::kMagic, sizeof(uncap::kMagic));
    g_shared->version = uncap::kVersion;
    g_shared->size    = uint32_t(sizeof(uncap::SharedMemory));
    return true;
}

DWORD WINAPI Boot(LPVOID) {
    ::InitializeCriticalSection(&g_logLock);
    g_logReady = true;
    ::InitializeCriticalSection(&g_patchLock);
    g_patchLockReady = true;

    ::QueryPerformanceFrequency(&g_qpcFreq);
    LogLine("attach pid %lu", (unsigned long)::GetCurrentProcessId());

    LoadConfig();
    g_flags.fetch_or(uncap::kFlagArmed, std::memory_order_relaxed);
    g_writerThread = ::CreateThread(nullptr, 0, &FakeWriter, nullptr, 0, nullptr);

    OpenShared();
    RaiseTimerResolution();

    for (int attempt = 0; attempt < 30; ++attempt) {
        if (HookSwapPaths() || HookVtables()) {
            LogLine("ready after attempt %d", attempt + 1);
            return 0;
        }
        ::Sleep(500);
    }
    return 0;
}

void Teardown() {
    g_fakeStop.store(true, std::memory_order_relaxed);
    if (g_writerThread) {
        ::WaitForSingleObject(g_writerThread, 1000);
        ::CloseHandle(g_writerThread);
        g_writerThread = nullptr;
    }
    RestoreFakePatch();
    Unhook();
    if (g_shared) {
        ::UnmapViewOfFile(g_shared);
        g_shared = nullptr;
    }
    if (g_map) { ::CloseHandle(g_map); g_map = nullptr; }
    if (g_logReady) {
        g_logReady = false;
        ::DeleteCriticalSection(&g_logLock);
    }
    if (g_patchLockReady) {
        g_patchLockReady = false;
        ::DeleteCriticalSection(&g_patchLock);
    }
}

} // namespace

// ------------------------------------------------------------- DllMain Entry ---
BOOL WINAPI DllMain(HINSTANCE inst, DWORD reason, LPVOID) {
    if (reason == DLL_PROCESS_ATTACH) {
        g_self = inst;
        ::DisableThreadLibraryCalls(inst);
        HMODULE pinned = nullptr;
        ::GetModuleHandleExW(GET_MODULE_HANDLE_EX_FLAG_PIN | GET_MODULE_HANDLE_EX_FLAG_FROM_ADDRESS,
                             reinterpret_cast<LPCWSTR>(&Boot), &pinned);
        HANDLE th = ::CreateThread(nullptr, 0, &Boot, nullptr, 0, nullptr);
        if (th) ::CloseHandle(th);
    } else if (reason == DLL_PROCESS_DETACH) {
        Teardown();
    }
    return TRUE;
}

// ------------------------------------------------------------------- Exports ---
extern "C" __declspec(dllexport) void OptimaizerUnhook() {
    g_disarmed.store(true, std::memory_order_relaxed);
    g_intervalSet.store(false, std::memory_order_relaxed);
    RestoreFakePatch();
    Unhook();
    g_flags.fetch_and(~(uncap::kFlagHookMask | uncap::kFlagUncapped | uncap::kFlagArmed),
                      std::memory_order_relaxed);
    if (g_shared) g_shared->flags = g_flags.load(std::memory_order_relaxed);
}

// Called through WH_CALLWNDPROC, so this must return immediately. Scanning
// modules or installing hooks here runs on the emulator's UI thread and is
// exactly what produced the "not responding" freeze. The worker does the work.
extern "C" __declspec(dllexport) int OptimaizerHook() {
    g_disarmed.store(false, std::memory_order_relaxed);
    g_intervalSet.store(false, std::memory_order_relaxed);
    RestoreFakePatch();
    ResetOverlayState();
    g_flags.fetch_or(uncap::kFlagArmed, std::memory_order_relaxed);
    g_rearmRequested.store(true, std::memory_order_relaxed);
    if (g_shared) g_shared->flags = g_flags.load(std::memory_order_relaxed);
    return 1;
}

// Runs in the target process and changes its own priority class. BlueStacks
// denies PROCESS_SET_INFORMATION to outside handles; this path needs no handle.
// Invoked through WH_CALLWNDPROC, so the requested class arrives in the
// CWPSTRUCT of the WM_NULL message (wParam 1 = above normal, 0 = normal).
extern "C" __declspec(dllexport) LRESULT CALLBACK OptimaizerSetPriority(int code, WPARAM wParam, LPARAM lParam) {
    bool above = wParam != 0;
    if (code == HC_ACTION && lParam) {
        const auto* cwp = reinterpret_cast<const CWPSTRUCT*>(lParam);
        above = cwp->wParam != 0;
    }

    const BOOL ok = ::SetPriorityClass(::GetCurrentProcess(),
        above ? ABOVE_NORMAL_PRIORITY_CLASS : NORMAL_PRIORITY_CLASS);
    if (ok) {
        if (above) g_flags.fetch_or(uncap::kFlagPriorityAbove, std::memory_order_relaxed);
        else       g_flags.fetch_and(~uncap::kFlagPriorityAbove, std::memory_order_relaxed);
    }
    if (g_shared) g_shared->flags = g_flags.load(std::memory_order_relaxed);
    return ok ? 1 : 0;
}

// Guaranteed `xor eax,eax; ret` for the WH_GETMESSAGE loader gadget. The C#
// injector scans executable sections for these bytes, so the sequence must live
// in code, not in .rdata (a const array would land there).
#if defined(_MSC_VER)
#pragma section(".gadget", read, execute)
extern "C" __declspec(allocate(".gadget")) const unsigned char g_loaderGadget[] = { 0x31, 0xC0, 0xC3 };
#else
extern "C" {
__asm__(
    ".section .text\n"
    ".globl g_loaderGadget\n"
    "g_loaderGadget:\n"
    ".byte 0x31, 0xC0, 0xC3\n"
    ".text\n"
);
}
#endif

extern "C" __declspec(dllexport) unsigned OptimaizerFpsVersion() {
    return uncap::kVersion;
}

extern "C" __declspec(dllexport) void SetFpsMultiplier(int multiplier, int enableJitter) {
    const int mult = (std::max)(1, (std::min)(10, multiplier));
    const int previous = g_multiplier.exchange(mult, std::memory_order_relaxed);
    g_fakeJitter.store(enableJitter != 0, std::memory_order_relaxed);
    g_fakeEnabled.store(multiplier > 0, std::memory_order_relaxed);

    // The factor is baked into the cave; ask the worker to reinstall it.
    if (previous != mult && g_patchActive.load(std::memory_order_acquire))
        g_repatchRequested.store(true, std::memory_order_relaxed);

    const int real = g_realFps.load(std::memory_order_relaxed);
    if (real > 0)
        g_displayFps.store((std::min)(9999, (std::max)(1, real * mult)), std::memory_order_relaxed);
}
