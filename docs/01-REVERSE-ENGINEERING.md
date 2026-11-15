# 01 · Reverse engineering

This document records the static study of `reference.exe` that informed the
rebuild. It is evidence-first: observed structure and strings, not guesses about
intent. No license logic was bypassed, no embedded payload was executed, and no
network endpoint was contacted with credentials.

## Sample identity

| Property | Value |
|---|---|
| File | `reference.exe` |
| Size | 922,112 bytes |
| SHA-256 | `296fc27720f2a2641a56de58d0d5880f5fca7ead36b1660c7ac5e2c3338310da` |
| Format | PE32+ executable, x64 |
| Subsystem | Windows GUI |
| Toolchain clues | Native C++, MinGW-w64 runtime imports |
| PDB path | `c:\Users\<builder>\OneDrive\Pictures\<project>\<project>V1\bin\x64\Release\reference.pdb` |

The PDB path is a build-environment artifact. It identifies the project family
and release configuration; it is not a runtime dependency.

## Import profile

The import table maps directly to the application's visible capabilities.

| Area | Evidence | Interpretation |
|---|---|---|
| Rendering | `d2d1.dll`, `dwrite.dll` | Custom Direct2D/DirectWrite interface, not a stock WinForms shell |
| Window chrome | `dwmapi.dll` | Dark title bar / corner attributes |
| Networking | `WININET.dll`, `urlmon.dll` | HTTP calls and URL handling |
| Configuration | `ADVAPI32.dll` registry and service APIs | Persistent settings and privileged operations |
| Cryptography | `ADVAPI32.dll` crypto APIs | Hashing / license material |
| Injection | `OpenProcess`, `VirtualAllocEx`, `WriteProcessMemory`, `CreateRemoteThread` | Remote DLL injection pipeline |

The injection trio is the strongest structural signal that the visual features
are not merely configuration writers. The binary carries dedicated payload
images for that pipeline.

## Configuration and endpoints

Observed strings and file access patterns:

- `C:\ProgramData\enigma.ini` — primary configuration.
- `%ProgramData%\enigma_fx.txt` — visual/effect configuration.
- `%ProgramData%\enigma_x.log` — runtime log.
- `https://licenseauth.help/api/1.3/` — license/authentication endpoint.

The `enigma_*` names and the license endpoint are specific to the original
product. Stellary.PX does not reuse the endpoint, the file names, or the license
protocol. Its configuration lives under `%APPDATA%\Stellary.PX` and its logs use
a dated, local-only format.

## Embedded payloads

Three PE images were carved from the outer executable by locating embedded
MZ/PE headers and reconstructing their sections. They were inspected statically
and never loaded.

| Payload | Size | Inferred role |
|---|---:|---|
| Core optimizer | 168,512 bytes | Process and system tweak worker |
| EGL/OpenGL hook | 58,368 bytes | Swap-interval / vsync interception |
| Shader post-process | 241,360 bytes | Frame post-processing and visual grading |

The sizes are recorded for identification. The images themselves are not
distributed with this project and are not required to build or run Stellary.PX.

## Anti-analysis behavior

String and import review showed:

- A list of known instrumentation process names (debuggers, memory scanners,
  network capture tools, managed decompilers, dumpers).
- Logic consistent with detecting and acting on those processes.
- A license gate tied to a remote endpoint.
- No evidence of a packer; the outer binary is directly inspectable.

Stellary.PX deliberately drops these behaviors. Its scan is read-only and its
diagnostics page explains that a process-name match is a warning, not proof of
misconduct. Nothing is terminated, hidden, or blocked.

## Interface reference

The original interface was reviewed only as visual reference:

- Near-black surfaces with brushed-metal highlights.
- A script wordmark paired with small-caps labels.
- A circular sigil ornament and a thin ring gauge.
- Monochrome cards, hairline dividers, and a single line chart.
- A compact sidebar with grouped navigation and a developer block.

Stellary.PX keeps the *material language* — black chrome, silver, engraving,
restraint — and replaces the layout, wordmark, sigil, icon set, typography
composition, and interaction model with original work. The source's hexagonal
"S" logo is not reused.

## Behavioral inventory

The observable feature set, independent of implementation details:

1. Locate the Android emulator process and installation.
2. Raise process priority while the emulator runs.
3. Request a finer system timer resolution.
4. Activate a high-performance or Ultimate Performance power scheme.
5. Write GameDVR / fullscreen-exclusive / Game Bar registry values.
6. Measure and delete emulator QML caches.
7. Inspect or replace BlueStacks kernel drivers.
8. Inject a renderer payload for frame pacing and vsync behavior.
9. Apply a shader-based visual grade.
10. Gate the app behind a remote license check.
11. Detect instrumentation tools.
12. Present all of it in a custom dark interface with live telemetry.

Items 1–7 and 12 are implemented in Stellary.PX. Items 8–9 are scoped as an
optional native renderer module; the current build compiles presets and reports
the renderer as not attached. Items 10–11 are intentionally removed or reduced to
read-only reporting. See [02 · Feature map](02-FEATURE-MAP.md).

## Ethics and reproducibility

- The sample was inspected statically on a controlled machine.
- No payload was executed, no remote service was authenticated against, and no
  credential or license material was extracted.
- Carved images remain outside the repository.
- The rebuild uses public Windows APIs and original code. It does not reproduce
  the source's license bypass, anti-debug behavior, or remote payload delivery.
- Claims about performance are framed as mechanisms and observed state, not
  guaranteed frame-rate gains.

This boundary is intentional: the goal is a useful, auditable emulator console,
not a functional clone of the original's offensive or licensing logic.
