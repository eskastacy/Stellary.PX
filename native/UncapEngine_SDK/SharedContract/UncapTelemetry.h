#pragma once
// ============================================================================
// UncapTelemetry.h
// Shared memory contract between UncapEngine.dll (inside target) and Client (outside)
// Shared mapped page name: "Local\\OptimaizerFps_<PID>"
// ============================================================================

#include <cstdint>

namespace uncap {

constexpr char     kMagic[8]  = { 'O', 'P', 'T', 'F', 'P', 'S', '0', '1' };
constexpr uint32_t kVersion   = 3;
constexpr int      kSamples   = 512;

// Telemetry & State Flags
enum : uint32_t {
    kFlagPresentHooked  = 1u << 0,   // DXGI SwapChain::Present patched
    kFlagPresent1Hooked = 1u << 1,   // DXGI SwapChain1::Present1 patched
    kFlagTearingAllowed = 1u << 2,   // DXGI_PRESENT_ALLOW_TEARING accepted
    kFlagTearingRefused = 1u << 3,   // Tearing refused (DXGI_ERROR_INVALID_CALL)
    kFlagTimerSet       = 1u << 4,   // High-precision 0.5ms timer granted
    kFlagTimerRefused   = 1u << 5,   // High-precision timer refused
    kFlagUncapped       = 1u << 6,   // SyncInterval forced to 0
    kFlagGlHooked       = 1u << 7,   // OpenGL (eglSwapBuffers / wglSwapBuffers) patched
    kFlagVkHooked       = 1u << 8,   // Vulkan (vkQueuePresentKHR) patched
    kFlagFakeFpsActive  = 1u << 9,   // Fake FPS overlay patcher active
    kFlagPriorityAbove   = 1u << 10,  // AboveNormal set from inside the process
    kFlagArmed          = 1u << 11,  // Engine armed (hooks + overlay allowed)

    kFlagHookMask       = kFlagPresentHooked | kFlagPresent1Hooked |
                          kFlagGlHooked | kFlagVkHooked,
};

// Memory layout written by the DLL and read by the installer / client
#pragma pack(push, 8)
struct SharedMemory {
    char     magic[8]       = {};        // Must match kMagic
    uint32_t version        = 0;         // Must match kVersion
    uint32_t size           = 0;         // sizeof(SharedMemory)

    uint32_t flags          = 0;         // Bitwise OR of flags above
    int32_t  syncInterval   = -1;        // What the app requested
    int32_t  forcedSync     = -1;        // What we forced (0 = VSync off)
    uint32_t refreshHz      = 0;         // Monitor refresh rate
    uint32_t width          = 0;         // Backbuffer width
    uint32_t height         = 0;         // Backbuffer height

    uint64_t frames         = 0;         // Total presents observed
    uint64_t dropped        = 0;         // Frame pacing spikes
    double   avgMs          = 0.0;       // Average frame time (ms)
    double   p95Ms          = 0.0;       // 95th percentile frame time (ms)
    double   minMs          = 0.0;       // Minimum frame time (ms)
    double   maxMs          = 0.0;       // Maximum frame time (ms)
    double   timerMs        = 0.0;       // Actual timer resolution granted
    int64_t  lastQpc        = 0;         // QueryPerformanceCounter timestamp
    int64_t  qpcFreq        = 0;         // QPC frequency

    uint32_t sampleCount    = 0;         // Valid entries in ring buffer
    uint32_t head           = 0;         // Ring buffer write head
    float    ring[kSamples] = {};        // Raw historical frame times (ms)
};
#pragma pack(pop)

} // namespace uncap
