# Stellary.PX 0.1.1

**Emulator detection and honest status.** A correctness release for the 0.1.x
console. No new capabilities and no change to what the app may modify: the same
reversible stages, with accurate reporting when the emulator or Windows denies
one.

## What changed

### BlueStacks is detected again

The 0.1.0 detector read the process path through `Process.MainModule`, which
BlueStacks 5.22 blocks even for elevated administrators. Detection now uses
`PROCESS_QUERY_LIMITED_INFORMATION` and `QueryFullProcessImageName`, which the
emulator still permits. The Emulator page shows the real family, PID, executable
path, install root, and current priority class.

Verified against BlueStacks 5.22.262.1001 / MSI App Player Engine
5.22.75.6322:

```text
BlueStacks 5
Running · PID 16968
C:\Program Files\BlueStacks_nxt\HD-Player.exe
Normal
```

### The headline no longer lies

0.1.0 always ended a run with `Pipeline engaged.` even when a stage failed. The
Overview now derives its state from the target and the stages that actually
applied:

| Situation | Headline |
|---|---|
| Launch, no run this session | `Engine stand-by.` |
| Run with no emulator | `Pipeline active · no emulator.` |
| Run with an emulator, all stages applied | `Pipeline engaged.` |
| Run with an emulator, one stage denied | `Pipeline partially engaged.` |
| After Restore | `Engine stand-by.` |

The status line counts real changes only. A skipped stage is not an applied
stage, so `3/4 steps applied` means three changes, not three non-failures.

### Blocked means blocked

BlueStacks 5.22 denies `PROCESS_SET_INFORMATION` to every external process;
elevation and `SeDebugPrivilege` do not change that. The priority stage reports
`Blocked by emulator protection`, the Overview keeps `Normal · boost blocked`
visible, and the other stages continue. No driver, injection, or handle-duplication
trick is used to force the change.

A protected executable path is displayed as `path protected by emulator` rather
than the misleading `no active process`.

### Restore is idempotent

- The prior power scheme is captured once, on the first successful activation,
  and cleared after a successful restore. Repeated runs no longer overwrite the
  original baseline.
- Restore with no saved baseline skips the power stage instead of forcing High
  Performance.
- Startup derives engagement from the current session, so stale registry
  backups cannot make the app claim a pipeline run that never happened.

## Verification performed

- Release build: 0 warnings, 0 errors.
- Service harness: **17 read-only checks** pass; **20 checks** pass with
  `--system`, including timer, power-plan, and registry round-trips.
- Live BlueStacks run: detection, path, and priority read; Start produced
  `Pipeline partially engaged.` with `3/4 steps applied · Blocked by emulator
  protection`.
- Live no-emulator run: Start produced `Pipeline active · no emulator.`;
  Restore returned `Engine stand-by.` and restored the previous scheme.
- Six deterministic UI captures reviewed, each with a matching
  `selected='<key>'` log line.
- Release folder and ZIP: `SHA256SUMS.txt` verification passes.
- Authenticode: `NotSigned` (expected; no CA certificate was supplied).
- Local antivirus scan: not performed. Windows Defender is disabled by policy on
  the build host, so no first-party scan result exists. Scan the archive with
  your own engine and compare against the published manifest.

## Signature status

This build was produced without a CA-issued code-signing certificate, so it is
**unsigned**. `Get-AuthenticodeSignature` reports `NotSigned`. Verify the SHA-256
manifest and build from source if you need stronger provenance. The release
pipeline supports real signing and labels a build signed only after `signtool`
and `Get-AuthenticodeSignature` both succeed.

## Known limitations

- **Priority changes can be denied.** BlueStacks 5.22 blocks them by design.
  The app reports the block; it does not bypass emulator protection.
- The native renderer module (EGL swap hook, frame pacing, shader grade) is not
  included. Visual parameters compile to JSON; the UI reports the renderer as
  not attached rather than faking an effect.
- Emulator-specific features target BlueStacks 5 and MSI App Player 5 on x64.
- Timer and priority effects vary by system and emulator build. The app reports
  observed state, not a guaranteed frame-rate gain.
- Driver repair requires a trusted driver file supplied by the user.
- The build is framework-dependent; the .NET 8 Desktop Runtime is required.

## Upgrading

0.1.1 supersedes 0.1.0. Settings, presets, and backups under
`%APPDATA%\Stellary.PX` and `%ProgramData%\Stellary.PX` are compatible. If an
older run stored a power baseline after the fixed overwrite bug, the saved GUID
may point at the scheme that was active during the last run; set the desired
scheme once with `powercfg /setactive` if Restore returns the wrong one.

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
