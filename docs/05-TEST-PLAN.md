# 05 · Test plan

The test strategy has four layers: build, service behavior, visual capture, and
release verification. System-changing checks are opt-in and always restore their
prior state.

## 1 · Build gate

```powershell
dotnet build Stellary.PX.sln -c Release
```

Expected: `0 Warning(s)`, `0 Error(s)`.

The solution contains the WPF app and the console harness. The app has no NuGet
package references; a restore should not introduce third-party binaries.

## 2 · Service harness

```powershell
dotnet run --project tests\Stellary.PX.Tests -c Release
```

Expected: `passed 18 | failed 0 | skipped 3`.

Read-only coverage:

| Check | What it proves |
|---|---|
| Branding defaults | Five links, HTTPS URLs, developer identity |
| Social link display | Query/scheme stripping for the settings list |
| Path layout | Data/log/backup locations are product-scoped |
| Settings defaults | Safe opt-ins and visual defaults |
| JSON store | Atomic replace, camelCase, round-trip |
| Checksum parser | `sha256sum` format, binary marker, comments, mismatch |
| Self integrity | Hash shape and manifest state |
| Power plan query | `powercfg` parsing and GUID shape |
| Timer query | `NtQueryTimerResolution` returns a usable value |
| System sample | CPU/RAM values stay in range |
| Process probe | Limited-query path and priority reads work on real processes |
| Machine fingerprint | Stable, formatted, not the raw MachineGuid |
| Emulator detection | Process/install integrity without mutation |
| Launch target resolution | Resolves an existing `HD-Player.exe` without launching it |
| Cache measurement | Scoped scan returns sane counts |
| Driver inventory | Full SHA-256 and Authenticode state |
| Instrumentation scan | Read-only process listing |
| Step semantics | Skip is not a failure; failure is not success |

### Reversible system pass

```powershell
dotnet run --project tests\Stellary.PX.Tests -c Release -- --system --verbose
```

Expected: `passed 21 | failed 0 | skipped 0`.

| Check | Mutation | Restore |
|---|---|---|
| Timer lock/release | Requests 1.0 ms in the harness process | Releases explicitly; asserts not held |
| Power plan | Activates Ultimate (or High Performance fallback) | Restores the captured prior GUID; asserts equality |
| Registry | Applies seven HKCU GameDVR/FSE values | Restores the service backup and cross-checks a manual snapshot |

Run this only on a machine you control. The power test skips when not elevated.
The registry test works in HKCU without elevation, but is still opt-in.

If a system test fails mid-mutation, the harness uses `finally` blocks and a raw
snapshot restore. Check `powercfg /getactivescheme` and the seven values before
continuing.

## 3 · Visual capture

```powershell
$exe = "src\Stellary.PX\bin\Release\net8.0-windows\Stellary.PX.exe"
$dir = "artifacts\capture-$(Get-Date -Format yyyyMMdd-HHmmss)"
Start-Process $exe -ArgumentList @("--capture", $dir) -Wait
Get-ChildItem $dir
```

Expected files:

```text
overview.png
emulator.png
system.png
visuals.png
diagnostics.png
settings.png
```

For a single page:

```powershell
Start-Process $exe -ArgumentList @("--capture", $dir, "--page", "system") -Wait
```

`Start-Process -Wait` is required: the executable is a GUI-subsystem binary, so
`& $exe` returns before capture completes.

Verify the log, not just the filenames:

```powershell
Select-String "$env:APPDATA\Stellary.PX\logs\stellary-*.log" -Pattern "selected='"
```

Every saved file should have a matching `selected='<key>'` line. A capture that
lost selection is invalid even if the PNG exists.

### Capture review checklist

- [ ] Title bar eyebrow and page title match the selected page.
- [ ] Sidebar selection is on the correct row.
- [ ] Overview chart has a non-empty trace and both CPU/RAM legends.
- [ ] Gauge value matches the CPU numeral.
- [ ] Pipeline rows show mechanism hints, not blank details.
- [ ] Emulator empty states read `no active process` / `not detected`; a blocked
      path reads `path protected by emulator`.
- [ ] Driver table shows full SHA-256 values and signature labels.
- [ ] System power name is readable and not truncated mid-word.
- [ ] Timer slider shows 0.5–2.0 ms endpoints.
- [ ] Reversibility rows show timer, power, registry state.
- [ ] Visual preset store shows the real `%APPDATA%` path.
- [ ] Renderer status says `not attached` when no module is present.
- [ ] Diagnostics log fills its card and the path footer is visible.
- [ ] Settings privacy card and developer links render without clipping.
- [ ] No control touches a card edge; no horizontal scrollbar appears.

## 4 · Manual behavior matrix

Run on Windows 10 and Windows 11, elevated and standard, with BlueStacks 5 and
MSI App Player 5 where available.

| Scenario | Expected |
|---|---|
| Emulator not running | Standby, `no active process`, no priority mutation; Launch boosted enabled |
| Emulator running | PID shown, Boost available, priority reads current class; Launch boosted disabled |
| Launch boosted, emulator closed | `HD-Player.exe` starts and retains `AboveNormal`; status reports the observed class |
| Launch boosted, launch fails | Clear failure detail; no priority or process mutation claimed |
| Emulator path denied to normal APIs | Path still resolved via limited query; page shows the real path |
| Emulator denies priority change | `Blocked by emulator protection`; stage excluded from applied count; headline `Pipeline partially engaged` |
| Boost then restore | Priority returns to the prior class |
| Restore without a saved power baseline | Power stage skips; no forced High Performance |
| Timer lock 0.5 / 1.0 / 2.0 | `system resolution` reflects the request; release drops it |
| Exit while timer held | Request released; log records exit |
| Power activate | Ultimate or High Performance fallback reported |
| Power restore | Prior scheme GUID restored exactly |
| Registry apply | Seven values written; ledger shows applied |
| Registry restore | Original values and presence restored |
| Registry partial failure | Remaining stages still run; failure detail shown |
| Cache empty | `0 B · 0 folders`, clean remains safe |
| Cache populated | Size measured before deletion; games preserved |
| Driver present | Version, 64-char SHA-256, trusted/unsigned label |
| Driver missing | `not installed`; table still renders |
| Driver replace, unsigned source | Rejected with a clear reason |
| Driver replace, trusted source | Backup created; copied hash verified |
| Non-elevated power action | User-facing failure, no crash |
| Settings reload | Values reflect the JSON file immediately |
| Social icon click | Default browser opens the configured HTTPS URL |
| Capture with `--page` | Exactly one correctly labeled PNG |

## 5 · Failure-path checks

- Rename `settings.json` to invalid JSON; app should load defaults and log a
  warning, not crash.
- Make `%APPDATA%\Stellary.PX` read-only; saves should fail gracefully and keep
  the session usable.
- Hold the timer, then close the window; verify release in the log and a clean
  exit.
- Start a second instance during a capture; captures must remain deterministic
  per process.
- Disconnect the network; all local pages must work. Only link clicks are
  affected.
- Run without BlueStacks installed; emulator page must show empty states and no
  exceptions.

## 6 · Release verification

```powershell
.\scripts\Build-Release.ps1 -Version 0.1.2
.\scripts\Verify-Release.ps1 -Path .\dist\Stellary.PX-0.1.2-win-x64\Stellary.PX.exe `
  -Sums .\dist\Stellary.PX-0.1.2-win-x64\SHA256SUMS.txt
```

If signed:

```powershell
Get-AuthenticodeSignature .\dist\...\Stellary.PX.exe |
  Format-List Status, StatusMessage, SignerCertificate
```

Expected: `Status = Valid`, a trusted chain, and the expected subject. An unsigned
build must report `NotSigned` and must not be described as signed in release
notes.

## 7 · CI scope

GitHub Actions runs:

- restore/build in Release
- the read-only harness
- a framework-dependent publish
- checksum generation and artifact upload

CI does **not** run `--system` (shared runners lack a stable power scheme and
should not mutate HKCU) and does not capture UI screenshots (no interactive
desktop). Those remain local gates before a release tag.

## 8 · Evidence retention

- Keep capture directories and the matching log excerpt with the release notes.
- Keep harness output for the exact commit being released.
- Keep the `SHA256SUMS.txt` alongside the ZIP.
- Never commit certificates, PFX files, passwords, or machine-specific logs.
