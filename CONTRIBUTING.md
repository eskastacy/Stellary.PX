# Contributing

Thanks for helping improve Stellary.PX. This project favors measured behavior,
small dependencies, and reversible changes over cleverness.

## Ground rules

- Keep the UI original. Do not copy assets, layouts, strings, or code from
  reference tool or other commercial tools.
- Do not add license bypasses, anti-debugging, process hiding, remote payload
  downloads, or telemetry.
- Do not simulate signatures or label an unsigned build as signed.
- No new NuGet dependencies without a written rationale. The shipping app has
  zero package references.
- Every system mutation needs a documented restore path and a test.
- Claims in the UI must match what the code actually does. If a feature is not
  attached, say so.

## Development setup

Requirements:

- Windows 10 1903+ / Windows 11 x64
- .NET 8 SDK
- Optional: BlueStacks 5 or MSI App Player 5 for emulator paths
- Optional: `signtool.exe` and a CA-issued certificate for signed releases

```powershell
git clone <repository-url>
cd Stellary.PX
dotnet build Stellary.PX.sln -c Debug
dotnet run --project tests\Stellary.PX.Tests -c Debug
```

Run the reversible system checks only on a machine you control:

```powershell
dotnet run --project tests\Stellary.PX.Tests -c Debug -- --system
```

The harness snapshots and restores the power scheme and seven HKCU values. It
prints the prior and restored state; investigate any failure before continuing.

## UI verification

Use capture mode instead of screen grabs. It renders the real WPF visual tree,
disables hit testing, verifies the selected page before saving, and writes
`selected='<key>'` to the session log.

```powershell
dotnet build src\Stellary.PX\Stellary.PX.csproj -c Debug
.\src\Stellary.PX\bin\Debug\net8.0-windows\Stellary.PX.exe --capture .\artifacts\shots
```

Review all six PNGs. A capture whose log says `lost selection` is invalid.

## Code style

- C# 12, nullable enabled, implicit usings.
- Services return `StepResult` values; do not throw across the UI boundary.
- Keep native interop in `Infrastructure/NativeMethods.cs` or a focused wrapper.
- Long work belongs on the thread pool; only bind results on the dispatcher.
- Use the palette and text styles in `Themes/`; no ad-hoc neon colors.
- Icons are SVG paths, never emoji.
- Prefer small, named methods over comments that restate code.

## Pull requests

Include:

1. What changed and why.
2. The exact commands you ran (build, tests, capture).
3. Before/after screenshots for visual changes.
4. Reversibility notes for anything that writes system state.
5. Confirmation that no secrets, certificates, or generated binaries are
   included.

## Release changes

Version bumps touch:

- `src/Stellary.PX/Stellary.PX.csproj` (`Version`, `AssemblyVersion`,
  `FileVersion`)
- `CHANGELOG.md`
- `docs/RELEASE-NOTES-<version>.md`
- `docs/06-RELEASE.md` if the pipeline changes

Run `scripts/Build-Release.ps1` and verify the produced manifest before tagging.
