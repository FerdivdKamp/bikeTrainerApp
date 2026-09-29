# AGENTS.md

## Project purpose

ErgTrainer is a personal .NET 8 Windows Forms application for indoor cycling.
It scans for and connects to BLE Tacx trainers and heart-rate monitors, reads
telemetry, displays live charts and a training timer, and contains ANT+ support
and CSV recording code. It is a desktop app, not a Windows Service.

An important goal is learning. Act as a pair programmer: explain meaningful
design choices, keep work small and reviewable, and do not make unrelated
improvements or refactors.

## Project layout

- `Program.cs` starts `MainForm` with the classic WinForms application loop.
- `MainForm.cs` owns the current UI composition and wires device and timer events.
- `Controls/` contains reusable WinForms controls, including the timer and charts.
- `Sensors/` contains Bluetooth scanning, BLE Tacx/heart-rate implementations,
  ANT+ heart-rate support, and device data models.
- `Recorders/` contains recording-related code.
- `ergtrainer.csproj` defines the WinForms target and NuGet dependencies.

There is currently no automated test project. Do not add one unless the task
warrants it; if adding testable domain logic, keep it independent of hardware
and Windows Forms.

## Technology and conventions

- Target: `net8.0-windows10.0.19041.0`.
- Output: Windows executable using Windows Forms.
- Platform target: x86. Preserve this unless a task explicitly requires changing
  it, because the ANT+ libraries and USB driver integration may depend on it.
- Main external integrations: `InTheHand.BluetoothLE` and Small Earth Tech ANT+
  packages.
- Namespace: `ErgTrainer`.

Follow the style already used in the affected files. Prefer straightforward,
idiomatic C# and focused methods over new layers, frameworks, or abstractions.
Do not add a NuGet package before checking whether the framework or existing
dependencies already meet the need.

## WinForms and async work

- WinForms controls must be accessed only on the UI thread. Device callbacks can
  arrive on another thread; marshal them with `InvokeRequired` and `BeginInvoke`
  (or an equally clear UI-thread mechanism) before changing controls.
- `async void` is appropriate only for WinForms event handlers and lifecycle
  overrides that require it. Keep their exception handling explicit so failures
  are visible to the user or debugger.
- Use `async`/`await` for Bluetooth, ANT+, file, and other I/O. Avoid `.Result`,
  `.Wait()`, and unnecessary `Task.Run`.
- Pass and honor `CancellationToken`s when an API and call path support them.
- Preserve responsive UI: never block the UI thread during scanning, connection,
  device discovery, or file operations.
- When forms or controls are disposed, unsubscribe event handlers and release
  timers, Bluetooth connections, ANT+ resources, and writers. Form close must
  leave device connections in a clean state.

## Device and telemetry behavior

BLE and ANT+ devices are unreliable. Handle unavailable hardware, incomplete
advertisements, failed connections, disconnects, malformed data, and cancellation
as expected operational conditions. Give the user clear status feedback and use
diagnostic logging where it helps investigate device issues.

Avoid tight retry loops and avoid treating a failed device action as a reason to
terminate the application. Keep transport/device concerns in `Sensors/`; keep
charting, timer, and UI state in the form or controls. Keep pure calculations
separate from Windows Forms and hardware APIs when practical.

Telemetry can be frequent. Avoid excessive allocations, LINQ, disk I/O, and
logging on each data event. Retain the existing chart data-point limits and
time-window behavior unless the requested change requires adjusting them.

## Recording and local data

Recording output is local user data. Do not delete, overwrite, or relocate it
without explicit direction. Keep recording I/O safe when data callbacks and UI
actions can occur concurrently, and make sure writers are flushed and disposed
when a recording stops or the app closes.

Do not commit device identifiers, credentials, API keys, or machine-specific
paths. If a new setting is needed, make it configurable and document it.

## Validation

Before changing code, inspect the relevant form/control/sensor code and its
event subscriptions so the lifecycle is understood.

After a code change, run the focused build when possible:

```powershell
dotnet build .\ergtrainer.sln
```

Hardware-dependent behavior cannot be proven by a build. State clearly when BLE,
ANT+, or physical trainer verification was not performed and give concise manual
steps when they are relevant. Add or update deterministic tests only where a test
project exists or where introducing one is justified by the task.

## Repository hygiene

The working tree may contain the user's local changes. Preserve unrelated edits.
Do not modify generated or local-machine artifacts such as `bin/`, `obj/`,
`.vs/`, `.idea/`, `*.csproj.user`, or `*.DotSettings.user`.

Do not commit, push, create branches, rewrite history, reset files, or edit
`.gitignore` unless explicitly requested. Keep edits limited to the requested
task and do not reformat unrelated files.
