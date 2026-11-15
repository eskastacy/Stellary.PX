# 02 · Feature map

How each observed capability of `reference tool` maps to Stellary.PX. Status
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
| 7 | Inspect and repair BlueStacks drivers | `Services/DriverService.cs`, `Infrastructure/Authenticode.cs` | Shipped, changed |
| 8 | Inject a renderer payload | — | Deferred |
| 9 | Frame pacing / vsync unlock | — | Deferred |
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
2. Attempts `SetPriorityClass(AboveNormal)` only while the emulator is running.
3. Reports `Blocked by emulator protection` on Win32 error 5 and leaves the
   process untouched.
4. Skips the stage as already applied when the target is already above normal,
   and skips restore when no boost is held.
5. Never installs a driver, injects code, or otherwise bypasses the protection.

### 7 · Driver repair

The original ships or fetches a BlueStacks driver payload. Stellary.PX instead:

1. Lists `BstkDrv_nxt.sys` and `BstkDrv_msi5.sys` with version, SHA-256, and
   Authenticode trust.
2. Accepts a replacement only from a local `.sys` file.
3. Requires administrator rights and a stopped emulator.
4. Requires the source file's Authenticode chain to be trusted.
5. Backs up the current driver to `%ProgramData%\Stellary.PX\backup`.
6. Copies the file and verifies the copied SHA-256.

This preserves the repair workflow without introducing a remote, mutable binary
channel. A signed-but-malicious driver remains a supply-chain risk; the UI says
so in [SECURITY.md](../SECURITY.md).

### 8–9 · Renderer module

The source carries two payload images for EGL/OpenGL interception and shader
post-processing. A faithful live implementation requires a native module that
attaches to the emulator's graphics API, negotiates swap intervals, paces frames,
and injects a post-process pass. That module is a separate engineering
deliverable, not a checkbox.

0.1.x therefore:

- Compiles the ten visual parameters into `%APPDATA%\Stellary.PX\visuals.json`.
- Provides Competitive, Cinema, and Clean baselines.
- Reports the renderer as **not attached** instead of implying a live effect.
- Ships no injector, no remote payload, and no anti-cheat evasion.

The UI and preset schema are stable enough for a future `Stellary.Render.dll`
without a redesign.

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

No remote payload download, no debugger kill, no process hiding, no obfuscation,
no anti-tamper. The build is inspectable and the network surface is limited to
links the user clicks.

## Non-goals

Stellary.PX does not:

- Bypass licenses, DRM, anti-cheat, or emulator integrity checks.
- Inject into processes other than an explicitly attached emulator renderer.
- Modify kernel drivers without a user-supplied, trusted file and a backup.
- Guarantee a frame-rate increase; it reports mechanisms and observed state.
- Collect telemetry or upload logs, presets, or hardware identifiers.

## Verification map

| Claim | Evidence |
|---|---|
| Read-only probes work | `Stellary.PX.Tests` default pass (17 checks) |
| Timer, power, registry are reversible | `Stellary.PX.Tests --system` (20 checks) |
| Process path/priority probes tolerate protected targets | `process probe` test |
| Priority denial is reported, not hidden | `priority` log line + Overview applied count |
| Checksum parser accepts sha256sum format | `checksum manifest parser` test |
| Emulator and driver inventory are real | `emulator detection`, `driver inventory` tests |
| UI pages render and are labeled correctly | deterministic capture logs + PNG review |
| Release manifest matches the binary | `scripts/Verify-Release.ps1` |
| Signature is real | `Get-AuthenticodeSignature`; no fake path exists |
