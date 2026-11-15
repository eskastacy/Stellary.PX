# Security policy

## Supported versions

| Version | Supported |
|---|---|
| 0.1.x | Yes |

## Reporting a vulnerability

Open a private security advisory on the repository, or contact the developer
through [Linktree](https://linktr.ee/eskastacy_). Do not open a public issue for
a vulnerability that could affect users before a fix is available.

Include:

- Affected version and Windows build.
- Steps to reproduce, with the smallest possible configuration.
- Observed and expected behavior.
- Whether the issue requires elevation.
- Any relevant log lines from `%APPDATA%\Stellary.PX\logs` with personal paths
  removed.

You should receive an acknowledgement within a few days. Please allow time for
a fix and a coordinated disclosure.

## Trust model

Stellary.PX is a local administration tool. It can change real system state, so
the security model is built around explicit, reversible actions:

- **User-initiated writes.** Power, registry, priority, cache, and driver
  changes happen only after a button press.
- **Capture before write.** Power GUIDs and registry values are recorded before
  modification and restored on request.
- **Process-scoped timer.** `NtSetTimerResolution` requests end with the process;
  the app also releases explicitly on exit.
- **No network mutation.** The app does not download drivers, updates, presets,
  or licenses. External links open in the default browser after an explicit
  click and must be absolute HTTP/HTTPS URIs.
- **No anti-analysis.** Instrumentation detection is read-only. The app does not
  terminate other processes, hide itself, block debuggers, or tamper with
  security tooling.
- **Inspectable build.** No packer, obfuscator, or encrypted payloads.
- **Integrity, not secrecy.** Releases publish SHA-256 manifests. Authenticode
  signatures are produced only with a real CA-issued certificate.

## Known boundaries

- **Unsigned community builds.** If no certificate is configured, releases are
  unsigned. Verify them with `SHA256SUMS.txt` and build from source if you need
  stronger provenance. An unsigned build is never presented as signed.
- **Driver replacement is powerful.** Only Authenticode-trusted `.sys` files are
  accepted, the previous driver is backed up, and the copy is hash-verified.
  A malicious signed driver remains a supply-chain risk outside this app's
  control. Prefer vendor-signed drivers from the emulator installation.
- **Elevation.** Power-scheme changes and driver repair require administrator
  rights. The app does not request elevation silently; the Settings page offers
  an explicit restart.
- **Third-party emulator.** BlueStacks and MSI App Player may change process
  names, install paths, driver names, or cache layouts. Detection failures are
  reported, not worked around with invasive techniques.
- **Protected emulator processes.** BlueStacks 5.22 denies
  `PROCESS_SET_INFORMATION` to external processes, including elevated
  administrators; `SeDebugPrivilege` does not change that. Priority and path
  reads use `PROCESS_QUERY_LIMITED_INFORMATION`, and a denied priority change is
  reported as `Blocked by emulator protection`. The app does not install a
  kernel driver, inject code, or otherwise bypass the emulator's protection.
- **Instrumentation scan.** A process-name match is a warning, not proof of
  malicious activity. The list is intentionally small and read-only.
- **No first-party antivirus certification.** Releases are not submitted to
  VirusTotal or a security vendor by default, and the build host may not have a
  scanner enabled. Verify `SHA256SUMS.txt`, inspect the source, or scan the
  archive with an engine you trust before deploying it widely.
- **Local data.** Settings, presets, and logs are readable by the current user.
  Backups under `%ProgramData%` follow the machine's ACLs. Do not store secrets
  in preset files.

## Release verification

```powershell
Get-FileHash .\Stellary.PX.exe -Algorithm SHA256
Get-Content .\SHA256SUMS.txt
.\scripts\Verify-Release.ps1 -Path .\Stellary.PX.exe -Sums .\SHA256SUMS.txt
Get-AuthenticodeSignature .\Stellary.PX.exe | Format-List Status,SignerCertificate
```

A valid signature requires `Status = Valid`, a trusted chain, and a signer
subject matching the release notes. The build script refuses to claim a
signature when none exists.
