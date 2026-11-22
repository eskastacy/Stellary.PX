# Changelog

All notable changes to Stellary.PX are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [0.1.3] — 2026-11-22

Live 2× FPS overlay and a single-file release.

### Added

- **Boost priority** on the Emulator page loads `UncapEngine.dll` into the
  running player through a thread-scoped `WH_GETMESSAGE` hook and an in-module
  `xor eax, eax; ret` gadget. The loader needs no `OpenProcess`, remote
  allocation, or remote thread, so it works with the stock protected driver.
- **Boost priority** also switches Windows to the Ultimate Performance plan so
  the CPU holds its boost clock; the previous scheme GUID is captured for
  **Restore normal**.
- **Restore normal** calls `OptimaizerUnhook` through a `WH_CALLWNDPROC` hook and
  returns the power plan; the next **Boost priority** re-arms through
  `OptimaizerHook`. Injected state is read from the
  `Local\OptimaizerFps_<pid>` telemetry mapping instead of the hidden module
  list.
- New `uncap payload exports` and `uncap payload hook gadget` harness checks.
- `OptimaizerSetPriority` export: the injected engine sets its own process class
  from inside the target when BlueStacks denies the outside handle. **Boost
  priority** and **Restore normal** both fall back to it.
- **Boost priority** also writes the active BlueStacks/MSI instance's high-FPS
  settings (`max_fps=240`, `enable_high_fps=1`, `enable_vsync=0`) to
  `bluestacks.conf` with a timestamped backup. The emulator applies them on the
  next start; an already-uncapped instance is reported and left untouched.
- The sidebar developer profile now ships with the developer portrait avatar
  (`assets/avatar.png`), cropped square for the circular badge.
- The release is now one self-contained `Stellary.PX.exe`. `UncapEngine.dll`,
  `UncapEngine.ini`, `branding.json`, and the avatar are embedded in the
  assembly; the payload is extracted to
  `%APPDATA%\Stellary.PX\payload\<version>` on first use.

### Changed

- The separate **Uncap FPS** and **Eject uncap** buttons are gone. **Boost
  priority** is the single arming action — priority, CPU clock, injection, and
  the high-FPS config in one click — and **Restore normal** is the single eject.
- The FPS overlay now shows **2x the live real FPS** instead of a fixed 1000.
  The 7-byte counter store is replaced with a jump to a code cave that multiplies
  `r8d` by the configured factor and stores the result. BlueStacks renders
  `r8d`, so the displayed value is derived from the rate it just measured — it
  rises and falls with the emulator (`Multiplier=` in `UncapEngine.ini`,
  default 2).
- The cave preserves flags for the conditional jump after the store and stores
  the same multiplied value the overlay renders, so the counter and the overlay
  always agree.
- Updating the overlay is the emulator's own store; the worker only mirrors the
  value for telemetry.
- Re-arming through `OptimaizerHook` restores the store and reinstalls the cave
  with the current factor. Repeated clicks no longer compound 200 → 400 → 800.
- `OptimaizerHook` only queues the re-arm; module scanning and hook installation
  run on the payload's worker thread instead of the emulator UI thread.
- The driver card is now a read-only inventory. The driver download, service
  stop/start, backup, copy, and boot-time rename paths are removed; the stock
  driver is never replaced or downgraded.
- The uncap engine now decodes hook prologues exactly, follows export thunks,
  and refuses prologues it cannot relocate. The previous length decoder missed
  immediate bytes and could build a corrupt OpenGL trampoline.

### Fixed

- The 7-byte FPS store and every inline hook prologue are now rewritten while
  all other threads in the process are suspended. A render thread can no longer
  fetch a half-written instruction, which was the freeze/crash path.
- The overlay immediate is written once per patch instead of being rewritten
  from the worker every 60 ms, removing a second cross-thread code write.
- Restore verifies that the recorded bytes are still the patch before writing;
  after a renderer restart the stale address is dropped and rescanned.
- Hook removal no longer frees a trampoline that a suspended thread may still be
  executing, and a failed `VirtualProtect` no longer marks the hook removed.
- Loading the payload no longer relies on `CreateToolhelp32Snapshot` module
  enumeration, which returns nothing for a protected `HD-Player.exe`.

## [0.1.2] — 2026-11-15

Launch-time priority for a closed emulator. This release adds a safe way to
start BlueStacks at `AboveNormal`; it is not a protection bypass.

### Added

- **Launch boosted** on the Emulator page starts a closed `HD-Player.exe`
  through the Windows `start /abovenormal` command. The process is created at
  `AboveNormal` before BlueStacks applies its runtime protection, and the page
  polls the real priority class to confirm what was retained.
- `EmulatorService.LaunchCandidate()` resolves `HD-Player.exe` from the known
  BlueStacks/MSI install roots without reading a protected target.
- The launch action is disabled while BlueStacks is running and explains that
  launch priority applies only to a new process.
- New `launch target resolution` harness check; the default pass now has 18
  checks and the `--system` pass has 21.

### Changed

- Emulator copy now separates launch-time scheduling from a live boost. The
  existing **Boost priority** action still reports
  `Blocked by emulator protection` when BlueStacks denies it.
- Launch status reports the observed class after start, including the failure
  case where the emulator did not retain `AboveNormal`.

### Security

- No kernel driver, handle manipulation, suspended-process technique, code
  injection, or anti-analysis behavior was added. A running BlueStacks remains
  protected, and the app continues to report that state honestly.

## [0.1.1] — 2026-11-15

Correctness release for emulator detection and pipeline reporting. No new
capabilities; no changes to what the app is allowed to modify.

### Fixed

- BlueStacks 5.22 detection no longer depends on `Process.MainModule`, which the
  emulator blocks even for elevated administrators. The image path and priority
  are read through `PROCESS_QUERY_LIMITED_INFORMATION` +
  `QueryFullProcessImageName`, so the Emulator page now shows the family, PID,
  executable path, install root, and current priority class.
- The Overview headline no longer prints `Pipeline engaged` after a denied
  stage. It distinguishes `Pipeline engaged`, `Pipeline partially engaged`,
  `Pipeline active · no emulator`, and `Engine stand-by`.
- Applied-step counts exclude deliberately skipped stages, so `3/4 steps
  applied` means three real changes rather than three non-failed results.
- A target that rejects priority changes is reported as
  `Blocked by emulator protection` instead of a generic elevation message.
- A protected executable path is shown as `path protected by emulator` instead
  of `no active process`.
- The power-plan baseline is captured once and cleared after a successful
  restore. Repeated runs no longer overwrite the original scheme GUID, and
  Restore without a saved baseline skips instead of forcing High Performance.
- Startup derives engagement from the current session only; stale registry
  backups can no longer make the app claim a pipeline run that did not happen.

### Changed

- Service harness: 17 read-only checks (new process-probe coverage) and 20
  checks with `--system`.
- Emulator priority is documented as best-effort. BlueStacks 5.22 denies
  `PROCESS_SET_INFORMATION` to external processes; the app reports the block and
  leaves the target untouched.

## [0.1.0] — 2026-11-15

First public community build. Framework-dependent, unsigned unless a release
certificate is configured.

### Added

- Black-chrome WPF shell with custom window chrome, grouped navigation,
  embedded OFL typography, vector iconography, and a developer profile.
- Overview dashboard: 2 Hz CPU/RAM pulse with a 60-second window, ring gauge,
  five-node pipeline ledger, and apply/restore controls.
- Emulator page: `HD-Player.exe` discovery, BlueStacks/MSI classification,
  `AboveNormal` priority boost and restore, QML cache measurement and cleanup.
- Driver inventory with file version, full SHA-256, and Authenticode trust;
  repair from a local trusted `.sys` with backup and copy verification.
- System tuning: active power scheme query, Ultimate Performance activation
  with hidden-scheme duplication, prior-GUID restore, `NtSetTimerResolution`
  0.5–2.0 ms lock/release, and seven reversible HKCU GameDVR/FSE values.
- Reversibility ledger and `%ProgramData%\Stellary.PX\backup` store.
- Visual tuning: ten-parameter preset compiler, Competitive/Cinema/Clean
  baselines, JSON persistence, export/reset, and honest renderer status.
- Diagnostics: runtime audit, self SHA-256, `SHA256SUMS.txt` verification,
  instrumentation scan, session log viewer, and report copy.
- Settings: start behavior, confirmations, data directories, elevation restart,
  privacy statement, and centralized branding/social links.
- Deterministic `--capture` screenshot mode that renders the real visual tree
  and verifies the active page before saving.
- Dependency-free service harness: 16 read-only checks plus three reversible
  system round-trips (`--system`).
- Release tooling: publish script, checksum manifest generation, verification
  helper, signing helper that requires a real certificate, and CI workflow.

### Changed

- Deliberate deviations from the source binary, documented in
  `docs/02-FEATURE-MAP.md`:
  - Driver repair uses a user-supplied signed driver instead of downloading a
    remote payload.
  - No license server, activation gate, HWID check-in, or anti-tamper.
  - Instrumentation scanning warns instead of blocking or terminating.
  - No debugger detection, process hiding, or obfuscation.

### Security

- Registry and power changes are captured before writing and restorable.
- Timer requests are process-scoped and released on exit.
- External links are restricted to absolute HTTP/HTTPS URIs.
- Release manifests use SHA-256; signing is never simulated.

### Known limitations

- The native renderer module (EGL swap hook, frame pacing, shader grading) is
  not part of 0.1.0. Presets compile and persist; live attachment is reported as
  unavailable rather than faked.
- The build is unsigned until a CA-issued code-signing certificate is supplied.
- Emulator-specific features target BlueStacks 5 / MSI App Player 5 on x64.
- Timer and priority effects depend on the OS and emulator build; the UI reports
  the observed state instead of promising a frame-rate gain.
