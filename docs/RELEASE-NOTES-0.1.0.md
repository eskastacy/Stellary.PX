# Stellary.PX 0.1.0

**First public community build.** A local-first Windows performance console for
Android emulators, rebuilt as original software from a documented behavioral
study.

## Highlights

- **Black chrome interface.** Custom WPF shell with engraved silver details,
  embedded OFL typography, vector icons, and a developer profile.
- **Live overview.** 2 Hz CPU/RAM pulse, 60-second window, ring gauge, and a
  five-node pipeline ledger with the mechanism named for every stage.
- **Emulator control.** Detects `HD-Player.exe`, boosts priority with restore,
  measures and clears QML caches, and inventories BlueStacks drivers with full
  SHA-256 and Authenticode state.
- **Reversible system tuning.** Ultimate Performance power scheme, 0.5–2.0 ms
  timer requests, and seven GameDVR/FSE registry values with captured backups.
- **Visual presets.** Ten parameters, three baselines, portable JSON, export and
  reset. Renderer status is reported honestly.
- **Diagnostics.** Self SHA-256, `SHA256SUMS.txt` verification, read-only
  instrumentation scan, session log, and report copy.

## Safety

- No remote driver downloads. Driver repair accepts only a local,
  Authenticode-trusted `.sys` file, backs up the current driver, and verifies the
  copy.
- No license server, activation gate, HWID check-in, or anti-tamper.
- No debugger killing, process hiding, obfuscation, or telemetry.
- Timer requests are process-scoped; power and registry writes are captured and
  restorable.
- Unsigned builds are labeled unsigned. No fake signatures.

## Install

1. Install the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)
   for the framework-dependent build.
2. Extract the release ZIP to a folder you control.
3. Verify `Stellary.PX.exe` against `SHA256SUMS.txt`:

   ```powershell
   .\scripts\Verify-Release.ps1 -Path .\Stellary.PX.exe -Sums .\SHA256SUMS.txt
   ```

4. Run `Stellary.PX.exe`. Elevate only when using power-plan or driver features.

Settings and logs go to `%APPDATA%\Stellary.PX`; backups go to
`%ProgramData%\Stellary.PX`.

## Signature status

This build was produced without a CA-issued code-signing certificate, so it is
**unsigned**. `Get-AuthenticodeSignature` reports `NotSigned`. Verify the SHA-256
manifest and build from source if you need stronger provenance. The release
pipeline supports real signing and will label a signed build only after
`signtool` and `Get-AuthenticodeSignature` both succeed.

## Known limitations

- The native renderer module (EGL swap hook, frame pacing, shader grade) is not
  included. Visual parameters compile to JSON; the UI reports the renderer as not
  attached rather than faking an effect.
- Emulator-specific features target BlueStacks 5 and MSI App Player 5 on x64.
- Timer and priority effects vary by system and emulator build. The app reports
  observed state, not a guaranteed frame-rate gain.
- Driver repair requires a trusted driver file supplied by the user.
- The build is framework-dependent; the .NET 8 Desktop Runtime is required.

## Verification performed

- Release build: 0 warnings, 0 errors.
- Service harness: 16 read-only checks pass; 19 checks pass with `--system`
  including timer, power-plan, and registry round-trips.
- Six deterministic UI captures reviewed with matching `selected='<key>'` log
  lines.
- Release folder: 24 files hashed; manifest verification passes.
- Authenticode: `NotSigned` (expected for this build).

## Links

- [Documentation](../docs/)
- [Reverse engineering](../docs/01-REVERSE-ENGINEERING.md)
- [Feature map](../docs/02-FEATURE-MAP.md)
- [Security policy](../SECURITY.md)
- [Changelog](../CHANGELOG.md)

Developer: **Eska Stacy** · EskaProject-X · `@jar.ajhi_`  
[Linktree](https://linktr.ee/eskastacy_) ·
[WhatsApp](https://whatsapp.com/channel/0029VbAowqoC1Fu3uTLcRe1n) ·
[TikTok](https://www.tiktok.com/@jarachyy_?_r=1&_t=ZS-99b76hsvJha) ·
[Discord](https://discord.gg/eZSNxNghu) ·
[Instagram](https://www.instagram.com/jar.ajhi_)
