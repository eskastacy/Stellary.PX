# 02 · Architecture

Stellary.PX is a framework-dependent .NET 8 WPF application with no NuGet
dependencies. It separates a thin presentation layer from UI-agnostic services so
the operational behavior can be tested without a window.

## Project layout

```text
Stellary.PX/
├─ src/Stellary.PX/              application (WinExe, net8.0-windows)
│  ├─ App.xaml / App.xaml.cs     startup, CLI options, capture, global errors
│  ├─ AppServices.cs             composition root
│  ├─ MainWindow.xaml            custom chrome + navigation shell
│  ├─ Views/                     six page views (XAML + code-behind)
│  ├─ ViewModels/                page state and commands
│  ├─ Services/                  operational layer (UI-free)
│  ├─ Infrastructure/            paths, JSON, logging, native, capture, branding
│  ├─ Models/                    persistence and domain records
│  ├─ Core/                      observable base, commands, StepResult
│  ├─ Controls/                  sparkline, ring gauge
│  ├─ Themes/                    palette, typography, control styles
│  ├─ assets/fonts/              embedded OFL fonts
│  └─ branding.json              identity and social links
├─ tests/Stellary.PX.Tests/      dependency-free console harness
├─ scripts/                      release, checksum, signing, verification
├─ docs/                         engineering documentation
└─ .github/workflows/            CI
```

## Layers

### Presentation

- `MainWindow` owns the sidebar, brand lockup, developer profile, title bar, and
  a `ContentControl` bound to the selected page object.
- `ShellViewModel` creates the six page view models, groups navigation, and
  switches `CurrentPage` while calling `Activate()` / `Deactivate()`.
- Each page view model exposes display strings, collections, and commands.
- `PageViewModel` is the common base: title, eyebrow, and activation hooks.
- `Core/ObservableObject` provides change notification; `RelayCommand` and
  `AsyncRelayCommand` provide input handling.

The view layer contains no system logic. A button either calls a view-model
command or a bound property setter; the view model delegates to a service.

### Application services

`AppServices` constructs:

```text
SettingsService
OptimizerEngine
  ├─ EmulatorService
  ├─ PriorityService
  ├─ TimerService
  ├─ PowerPlanService
  ├─ RegistryTweaksService
  ├─ CacheService
  ├─ DriverService
  └─ ToolScanService
BrandingConfig
```

`OptimizerEngine.ApplyAsync()` runs the pipeline on the thread pool:

```text
detect target
  → priority (if running and not already above normal)
  → timer lock (if enabled)
  → power plan (if enabled, capturing prior GUID once)
  → registry (if enabled, capturing prior values)
  → save settings
```

`RestoreAsync()` reverses only the stages it applied: priority when a boost is
held, timer, saved power plan, and registry. A stage with nothing to restore is
reported as skipped, not as success.

### Infrastructure

| Type | Responsibility |
|---|---|
| `Paths` | Single source of truth for data, log, backup, and manifest locations |
| `JsonStore` | Atomic JSON load/save with camelCase and case-insensitive reads |
| `AppLog` | Thread-safe append-only daily log; never throws into callers |
| `ShellRunner` | Bounded external process execution, URL opening, elevation |
| `NativeMethods` | Timer APIs, system sampling, DWM attributes, limited-query process probes, elevation check |
| `Authenticode` | `WinVerifyTrust` wrapper with no UI and no network validation |
| `CaptureMode` | Deterministic visual-tree capture for UI verification |
| `BrandingConfig` | Product identity, developer profile, social links |
| `IntegrityService` | SHA-256 self-hash and manifest verification |

### Domain and persistence

- `StepResult` is the outcome of one pipeline stage: label, success flag, detail,
  and a `Skipped` marker. A skip is not a failure, but it is not an applied
  change either; the UI counts only stages that actually ran.
- `AppSettings` stores opt-ins, the last power GUID, registry backups, and visual
  parameters.
- Settings are saved after every apply/restore and after behavior toggles.

## Threading

- UI work stays on the WPF dispatcher.
- `OptimizerEngine` methods wrap their bodies in `Task.Run`.
- `AsyncRelayCommand` prevents re-entry while a command is running.
- The overview samples system counters on a 500 ms `DispatcherTimer`; the
  sampling itself is cheap and non-blocking.
- Diagnostics refreshes its audit on the thread pool and marshals the strings
  back through the dispatcher.
- The timer request is process-scoped. `App.OnExit` releases it explicitly; the
  OS also drops it when the process ends.
- No background service, scheduled task, or hidden window is created.

## Data flow: apply

```text
Button
  → OverviewViewModel.StartCommand
    → OptimizerEngine.ApplyAsync (Task.Run)
      → Services.* returns StepResult
      → Settings.Save()
    ← RunReport
  → ViewModel updates pipeline rows, status, and gauges (dispatcher)
```

A failure in one stage does not abort the rest. The pipeline reports each result
independently, so a blocked registry write does not prevent a timer release or a
power restore.

## Data flow: capture

`--capture <dir> [--page <key>]`:

1. Creates the output directory.
2. Sets `IsHitTestVisible = false` and `Topmost = true` so stray clicks and focus
   changes cannot alter the page.
3. Iterates the requested pages.
4. Selects the page, waits for timers to produce samples (9 s for overview, 0.7 s
   otherwise), and re-selects if the selection changed.
5. Renders the real `Window` visual tree with `RenderTargetBitmap`.
6. Saves `<key>.png` and logs `selected='<key>'`.

This removes screen occlusion, DPI scaling, and click-through problems from UI
verification. The log is the source of truth for which page was captured.

## Persistence

| Path | Contents |
|---|---|
| `%APPDATA%\Stellary.PX\settings.json` | Behavior opt-ins, last power GUID, registry backups |
| `%APPDATA%\Stellary.PX\visuals.json` | Preset name and ten parameters |
| `%APPDATA%\Stellary.PX\logs\stellary-YYYYMMDD.log` | Daily session log |
| `%ProgramData%\Stellary.PX\backup\` | Timestamped driver backups |
| `<app>\SHA256SUMS.txt` | Optional release manifest |

The JSON store writes `<file>.tmp` and uses `File.Replace` (or `File.Move` for a
first write), so readers never observe a partial document.

## Native interop

All P/Invoke lives in focused wrappers:

- `NtSetTimerResolution` / `NtQueryTimerResolution` — timer requests and readout.
- `GetSystemTimes`, `GlobalMemoryStatusEx` — CPU and memory sampling.
- `DwmSetWindowAttribute` — dark title bar and corner preference.
- `WinVerifyTrust` — Authenticode trust for drivers.
- WindowsPrincipal — elevation state.

No function pointers are patched, no remote memory is allocated, and no code is
injected by the managed application.

## Failure handling

- Service methods catch and translate exceptions into `StepResult` failures.
- `AppLog` catches all I/O failures; logging cannot take down the app.
- `App.DispatcherUnhandledException` logs the stack, shows a message, and marks
  the event handled so the session can continue.
- Capture errors log and exit with code 1 rather than writing a misleading image.
- External processes run with a timeout and no shell window.

## Build configuration

- `net8.0-windows`, `UseWPF`, nullable and implicit usings enabled.
- Framework-dependent by default; no ReadyToRun, trimming, or single-file
  packaging (those complicate inspection and WPF resource resolution).
- Version metadata is set in the csproj; the test harness and release scripts
  read it from there or take it as a parameter.
- Fonts are embedded as WPF resources and referenced with per-file pack URIs so
  each weight resolves to the intended face.

## Extension points

- **Renderer module.** The preset schema and `VisualsViewModel` status fields are
  ready for an optional native DLL. Attachment must remain explicit and
  reversible.
- **Additional emulators.** Add detection roots and classification in
  `EmulatorService`; keep cache discovery scoped to named `qmlcache` folders.
- **Additional registry tweaks.** Extend the tuple list and the test snapshot in
  lockstep; backups and restore already generalize.
- **Additional checks.** Add a `Run(...)` test to the harness; the default pass
  must remain read-only.
