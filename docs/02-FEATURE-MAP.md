# 02 · Feature map

How each observed capability in the reference build maps to Stellary.PX. Status
values:

- **Shipped** — implemented and reachable in the current build.
- **Shipped, changed** — implemented with a deliberate safety or transparency
  change.
- **Partial** — the durable half is implemented; the live pipeline is not.
- **Deferred** — designed but not part of the current build.
- **Removed** — intentionally not reproduced.

| # | Source capability | Implementation | Status |
|---:|---|---|---|
| 1 | Discover the emulator process and install | `Services/EmulatorService.cs`, `ViewModels/EmulatorViewModel.cs` | Shipped |
| 2 | Raise emulator process priority | `Services/PriorityService.cs` | Shipped, changed |
| 3 | Request finer timer resolution | `Services/TimerService.cs`, `Infrastructure/NativeMethods.cs` | Shipped |
| 4 | Activate Ultimate Performance | `Services/PowerPlanService.cs` | Shipped |
| 5 | GameDVR / FSE / Game Bar registry values | `Services/RegistryTweaksService.cs` | Shipped |
| 6 | Measure and clear QML caches | `Services/CacheService.cs` | Shipped |
| 7 | Inspect BlueStacks drivers | `Services/DriverService.cs`, `Infrastructure/Authenticode.cs` | Shipped, changed |
| 8 | Inject a renderer payload | `Services/UncapService.cs`, `native/UncapEngine_SDK` | Shipped, changed |
| 9 | Frame pacing / vsync unlock | `native/UncapEngine_SDK`, `Services/UncapService.cs` | Shipped, changed |
| 10 | Shader post-process grade | `ViewModels/VisualsViewModel.cs` (preset compiler only) | Partial |
| 11 | Remote license / activation | — | Removed |
| 12 | Detect instrumentation tools | `Services/ToolScanService.cs` | Shipped, changed |
| 13 | Runtime log and diagnostics | `Infrastructure/AppLog.cs`, `ViewModels/DiagnosticsViewModel.cs` | Shipped |
| 14 | Optimize on launch | `Models/AppSettings.cs`, `App.xaml.cs` | Shipped |
| 15 | Custom dark interface and live telemetry | WPF shell, views, `Controls/Sparkline.cs`, `Controls/RingGauge.cs` | Shipped (original design) |
| 16 | ProgramData `enigma_*` configuration | `Infrastructure/Paths.cs`, `Infrastructure/JsonStore.cs` | Shipped, changed |
| 17 | Download remote payloads | — | Removed |
| 18 | Anti-debug / anti-tamper behavior | — | Removed |

## Notes per deviation

### 2 · Process priority

Priority changes are best-effort by design. Windows normally grants
`PROCESS_SET_INFORMATION` to an elevated caller, but BlueStacks 5.22 denies it to
every external process, including administrators with `SeDebugPrivilege`
enabled. Stellary.PX therefore:

1. Reads the target image and priority with `PROCESS_QUERY_LIMITED_INFORMATION`,
   which BlueStacks still permits.
2. Offers **Launch boosted** for a closed emulator. It starts `HD-Player.exe`
   through the standard Windows `start /abovenormal` command, so the process is
   created above normal before BlueStacks applies runtime protection. This is
   launch-time scheduling, not a bypass.
3. Attempts `SetPriorityClass(AboveNormal)` directly while the emulator is
   running.
4. If the direct handle is denied, the uncap engine sets the class from inside
   the target through the `OptimaizerSetPriority` export. No handle, token, or
   driver is needed; the call runs on the target's UI thread through the same
   `WH_CALLWNDPROC` path used by the other exports.
5. Reports the class that was actually applied, including which path set it.
   Restore uses the same direct-then-in-process order.
6. Never installs a driver or steals handles to move the process class. The
   uncap payload is separate and attaches only to the emulator (§8–9).

### 7 · Driver inventory

The original replaces the BlueStacks kernel driver with a downloaded build.
Stellary.PX deliberately does not:

1. Lists `BstkDrv_nxt.sys` and `BstkDrv_msi5.sys` with version, SHA-256, and
   Authenticode trust. Nothing is downloaded, replaced, staged, or rebooted.
2. The uncap engine works with the stock driver. The 2026 `BstkProtect` already
   grants `OpenProcess` but strips `PROCESS_VM_*` and `PROCESS_CREATE_THREAD` from
   the handle, so swapping the driver is not required and no longer offered.
3. Driver rows stay read-only evidence for support: family, path, version, hash,
   signature.

### 8–9 · Renderer payload and frame pacing

`native/UncapEngine_SDK` is a native payload built into `UncapEngine.dll` and
shipped next to `Stellary.PX.exe`:

1. **Load** — the client maps the DLL into its own process with
   `LoadLibraryEx(DONT_RESOLVE_DLL_REFERENCES)` and installs a thread-scoped
   `WH_GETMESSAGE` hook whose procedure is an in-module `xor eax, eax; ret`
   gadget. A `WM_NULL` post forces the system to map the payload into the
   emulator. No `OpenProcess`, `VirtualAllocEx`, `WriteProcessMemory`, or
   `CreateRemoteThread` is used, which is why the protected driver cannot block
   it.
2. **Boot** — the payload starts from `DllMain`, pins itself, opens the
   `Local\OptimaizerFps_<pid>` telemetry page, requests a 0.5 ms timer, locates
   the native BlueStacks FPS writer (signature scan over `HD-Opengl-Native.dll`,
   `libOpenglRender.dll`, `HD-Player.exe`), and replaces the 7-byte store with a
   5-byte jump to a code cave. The cave multiplies `r8d` (the rate BlueStacks
   just measured) by the factor and writes the result to the counter; BlueStacks
   renders `r8d`, so both paths show the multiplied rate and the overlay rises
   and falls with the emulator. The cave preserves flags, because the next
   instruction is a conditional jump. Changing the factor rewrites the cave;
   there is no sampling window and no swap-interval wait. Every live-code rewrite
   (the FPS store, the cave, and each inline hook prologue) happens with all
   other threads suspended; hook trampolines stay allocated after an unhook
   because a suspended thread may still be executing one. Inline hooks decode
   the prologue exactly, follow export thunks, and refuse any prologue
   containing a relative branch or RIP-relative operand rather than building a
   corrupt trampoline.
3. **Disarm / re-arm** — **Restore normal** calls the `OptimaizerUnhook` export
   through a `WH_CALLWNDPROC` hook delivered with `SendMessageTimeout`; **Boost
   priority** calls `OptimaizerHook` the same way when telemetry already exists.
   `OptimaizerHook` only restores the patch and queues the re-arm; module
   scanning and hook installation run on the payload's worker thread, never on
   the emulator UI thread. The module stays resident until the emulator exits.
4. **Detect** — injected state is read from the telemetry mapping, not
   `Toolhelp` module enumeration, which the protected process hides.

The shader post-process pass remains deferred. The visual preset surface still
compiles to `%APPDATA%\Stellary.PX\visuals.json` and the renderer reports **not
attached** for grading; the UI and preset schema remain stable for a future
`Stellary.Render.dll`.

### 10 · Visual grading

The parameter surface exists and persists. The durable contract is the JSON
preset; the live GPU pass is deferred with the renderer module. A preset export
is useful today for external tooling and for reproducing a look later.

### 11 · Licensing

The original enforces a remote license. This community build has no key, no
HWID check-in, no endpoint, and no gate. The machine fingerprint that remains is
a local, salted HMAC used only for support/diagnostics; it is never transmitted.
Removing the license path also removes a remote availability and privacy
dependency.

### 12 · Instrumentation detection

The original's tool list is reused only as a read-only name match. Stellary.PX
does not terminate, suspend, hide from, or block the listed processes. The
Diagnostics page labels the result as a warning and copies it into the support
report. This keeps the informational value without the retaliatory behavior.

### 16 · Configuration location

`%ProgramData%\enigma.ini` and friends are replaced by:

- `%APPDATA%\Stellary.PX\settings.json`
- `%APPDATA%\Stellary.PX\visuals.json`
- `%APPDATA%\Stellary.PX\logs\stellary-YYYYMMDD.log`
- `%ProgramData%\Stellary.PX\backup\` for machine-scoped backups

The JSON store writes to a temporary file and replaces atomically, so a crash
cannot truncate the live settings file.

### 17–18 · Removed behavior

No silent payload download, no debugger kill, no process hiding, no obfuscation,
no anti-tamper. The build is inspectable and makes no outbound requests.

## Non-goals

Stellary.PX does not:

- Bypass licenses, DRM, anti-cheat, or emulator integrity checks.
- Inject into processes other than an explicitly attached emulator renderer.
- Replace, patch, or downgrade the emulator's kernel driver.
- Guarantee a frame-rate increase; it reports mechanisms and observed state.
- Collect telemetry or upload logs, presets, or hardware identifiers.

## Verification map

| Claim | Evidence |
|---|---|
| Read-only probes work | `Stellary.PX.Tests` default pass (19 checks) |
| Timer, power, registry are reversible | `Stellary.PX.Tests --system` (22 checks) |
| Launch target resolves to the real executable | `launch target resolution` test |
| Process path/priority probes tolerate protected targets | `process probe` test |
| Priority denial is reported, not hidden | `priority` log line + Overview applied count |
| Checksum parser accepts sha256sum format | `checksum manifest parser` test |
| Emulator and driver inventory are real | `emulator detection`, `driver inventory` tests |
| Payload exports and hook gadget exist | `uncap payload exports`, `uncap payload hook gadget` tests |
| UI pages render and are labeled correctly | deterministic capture logs + PNG review |
| Release manifest matches the binary | `scripts/Verify-Release.ps1` |
| Signature is real | `Get-AuthenticodeSignature`; no fake path exists |
