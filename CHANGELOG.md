# Changelog

All notable changes to Stellary.PX are recorded here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html).

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
