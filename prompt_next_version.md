# ErgTrainer next version: phased plan and agent prompts

Last reviewed: 2026-10-21. This is a backlog and set of prompts, not a claim that the work below is implemented. Each numbered phase is small enough to become a GitHub issue or a separate agent task. Recheck tool and package versions when starting a phase.

## Goal and recommended direction

Keep the existing Windows app working while adding a useful Android APK. Upgrade the Windows app to **.NET 10 LTS** soon: .NET 8 support ends on **2026-11-10**, while .NET 10 is supported through **2028-11-14** [1]. Keep WinForms for the near-term Windows release. Build the Android app as a separate Flutter application, sharing documented behavior and file formats rather than assuming WinForms or its Windows BLE library can run inside Flutter. Decide later whether a Flutter Windows rewrite earns its cost; Flutter supports Windows desktop on x64 and Arm64 [4]. The Windows project now targets x64 because ANT+ support was dropped in favor of BLE.

The first Android milestone is a **foreground-use, offline** app that connects to a real BLE trainer and heart-rate monitor, shows live power/cadence/speed/heart rate and a timer, and can make an installable APK. Trainer resistance control and recording should be separate follow-on issues so an initial Android build does not depend on the proprietary Tacx control path. No cloud account or phone-to-PC connection is assumed.

## What the repository contains today

| Area | Observed state | Planning consequence |
| --- | --- | --- |
| Windows app | One `net8.0-windows10.0.19041.0` WinForms executable, `x64`, with UI built in `MainForm.cs`. | Retarget the framework first; preserve x64 and the Windows API floor. |
| Dependencies | `InTheHand.BluetoothLE` 4.0.43. The unused ANT+ packages and their supporting `Microsoft.Extensions` references were removed. | Audit the remaining package for compatibility, advisories, and newer versions before changing it. |
| Trainer BLE | `BleTacxTrainer` handles FTMS/Cycling Power telemetry, CSC-derived cadence and speed, and Tacx FE-C-over-BLE or FTMS control discovery. | Record the actual GATT services, characteristic behavior, and trainer model before porting control to Android. Never infer working resistance control from successful discovery alone. |
| HRM BLE | `BleHeartRateSensor` reads the standard Heart Rate characteristic, using notifications and polling. | Android needs its own BLE connection, permission, notification, and cleanup path; the packet format can be specified and tested independently. |
| Lifecycle | `MainForm.OnFormClosing` now awaits BLE cleanup before closing and reacts to a lost trainer connection. BLE polling tasks are still started without being tracked/awaited. | Finish cancellation and polling lifecycle work before broad changes. |
| Charts and timer | `TimeSeriesChart` is a custom WinForms control with a 1,000-point cap and time window. `TrainingTimerControl` uses `Stopwatch` and a WinForms timer. Main window has fixed pixel positioning and is not resizable. | Specify display behavior for Android; improve desktop layout separately without changing telemetry rules. |
| Local data | `SignalRecorder` writes raw BLE CSV under Documents/ErgTrainer/Recordings. `DataRecorder` exists but is not wired into `MainForm`; do not treat it as an active workout-recording feature. | Preserve existing CSV files and schema. Decide separately what Android records and where users can export it. |
| Tests and future features | A small hardware-independent session check project exists in `Tests/`. The Session Builder now supports ERG running and Simulation editing/preview; Simulation hardware start remains disabled until slope control is verified. ANT+ support has been removed. | Keep session tests and BLE hardware validation distinct from Android porting. |

The repository was inspected as source. Physical BLE behavior has **not** been verified by this plan. Older task prompts (`prompt_bluetooth_control.md` and `prompt_training_session_builder.md`) still mention x86 or ANT+; those constraints are superseded by the current BLE-only, x64 project configuration.

## Execution rules for every phase

Read `agents.md` and relevant source before editing. Preserve the user's unrelated changes and local recordings. Keep each phase in its own reviewable change. Do not commit, push, create branches, or edit `.gitignore` unless explicitly asked. For Windows code, run `dotnet build .\ergtrainer.sln`; report warnings and hardware checks separately. For Flutter code, run the available Flutter analyze/test/build commands and test BLE on a physical Android device. Document any manual result and exact device/Android version without committing device identifiers. If a prerequisite is missing, finish the useful independent work and report the blocker precisely.

Suggested GitHub issue format: use each phase title below as the issue title; copy its **Prompt** and **Done when** sections into the body; add links to the preceding issue. A phase is complete only when its acceptance checks are met. Later phases can refine the plan based on evidence from earlier ones.

## Phase 0 — Establish a reproducible baseline

**Depends on:** nothing. **Priority:** immediate. **Issue title:** `Document Windows baseline and Android feature scope`.

**Prompt:** Inspect the current solution, `MainForm`, BLE sensor paths, timer, charts, recorders, README, and package references. Record a clean baseline build result, warnings, installed SDKs, and the existing user-visible flows. Make a short capability matrix for Windows and the proposed Android MVP: scanning, trainer telemetry, HRM, timer, ERG target, charts, raw recording, workout recording, and background use. Mark each as existing, required for Android MVP, later, or out of scope. Confirm the actual trainer model, Android device/API range, and whether the APK is for personal sideloading or store distribution when those facts become available. Do not change implementation code in this phase.

**Done when:** A reviewer can reproduce the baseline build and tell exactly which Android features the first APK will contain. Unknown hardware behavior is labeled unknown. Include the current ERG Session Builder in the Windows capability inventory, and mark Simulation hardware execution as unavailable until verified.

## Phase 1 — Upgrade Windows from .NET 8 to .NET 10 LTS

**Depends on:** Phase 0. **Priority:** before 2026-11-10. **Issue title:** `Retarget WinForms app to .NET 10 LTS`.

**Prompt:** Follow Microsoft's WinForms upgrade guidance and review .NET 9 and 10 breaking changes [2][3]. Change the target to `net10.0-windows10.0.19041.0`, preserving `WinExe`, WinForms, x64, and the existing Windows API floor. Audit the remaining NuGet reference for use, current support, security notices, and .NET 10 compatibility; update only when needed and justify the change. Update build instructions and target-version mentions in README/`agents.md` as appropriate. Build and smoke-test scan, trainer/HRM connect, timer/charts, ERG command availability, raw CSV start/stop, and shutdown on Windows with hardware where possible.

**Done when:** The solution builds on a supported .NET 10 SDK; the app starts and closes cleanly on Windows; x64 remains intact; changed dependencies and remaining warnings are documented. Any behavior requiring physical hardware is explicitly marked unverified until tried. This phase does not redesign the UI.

## Phase 2 — Stabilize device and recording lifecycles

**Depends on:** Phase 1. **Priority:** high before Android protocol work. **Issue title:** `Make Windows BLE shutdown and device state reliable`.

**Prompt:** Make focused fixes to the current Windows flow. Track scan/connection/poll operations and pass cancellation where supported; guard UI callbacks during closing; unsubscribe all form/device/timer handlers and finish cleanup before disposal. Handle failed and partial connections, disconnection during recording, duplicate connect attempts, and command timeout without leaving controls in a false connected state. Review `SignalRecorder` write/stop concurrency and avoid excessive per-packet disk work if profiling shows a problem; preserve existing CSV files and columns. Determine whether the dormant `DataRecorder`/`TacxDevice` code is unused or planned, and document the decision before removal.

**Done when:** Repeated connect/disconnect/close cycles and failed scans are safe; raw recording flushes on stop/close; UI remains responsive; diagnostic output identifies failures without logging every telemetry packet. A physical-device checklist records results or outstanding checks.

## Phase 3 — Define portable behavior and choose the Android BLE boundary

**Depends on:** Phases 0 and 2. **Issue title:** `Specify telemetry, control, and file contracts for Android`.

**Prompt:** Extract a short, versioned specification from the working Windows behavior: units and timestamps; Cycling Power, FTMS Indoor Bike Data, CSC, and Heart Rate parsing; missing-field behavior; chart time window; timer pause/resume; raw CSV columns; and the exact supported trainer-control paths. Use sanitized sample payloads or synthetic fixtures. Add focused deterministic tests for parsing and calculations in code independent of WinForms and hardware where that improves confidence. For Flutter, compare a maintained BLE plugin against a small Android Kotlin platform-channel implementation for the required GATT services, notifications, connection lifecycle, and possible Tacx FE-C writes. Record chosen approach, platform support, maintenance status, and known limits. Keep UI, transport, and parsing distinct; C# code cannot be assumed directly reusable in Dart.

**Done when:** An Android implementer has example bytes and expected values, a clear feature boundary, and a justified BLE implementation choice. Any proprietary Tacx command remains gated on documented protocol evidence and physical validation.

## Phase 4 — Prove BLE on a physical Android device

**Depends on:** Phase 3. **Issue title:** `Flutter Android BLE trainer and HRM spike`.

**Prompt:** Create a small Flutter Android project within this repository using the stable SDK available at execution time. Implement runtime Bluetooth scan/connect permissions for the chosen Android API range, service discovery, notifications, a visible connect/disconnect state, and clean disposal [5]. Connect to the actual trainer and HRM on a physical phone. Show decoded live values and a diagnostic view with sanitized service/characteristic IDs and errors. Start with read-only telemetry. Test permission denied, Bluetooth off, device absent, connection loss, and reconnect. Keep Android-specific BLE code behind a narrow interface so the UI is not tied to a plugin. Avoid background training claims until Android's connected-device/foreground-service rules are explicitly implemented [6].

**Done when:** A physical Android phone receives trainer and HRM values that plausibly match Windows, and can disconnect/reconnect without stale subscriptions. Emulator-only success is insufficient. If the trainer exposes only proprietary telemetry/control that the chosen library cannot support, record the exact blocker and adjust Phase 5 scope.

## Phase 5 — Build the Android foreground workout MVP

**Depends on:** Phase 4. **Issue title:** `Build Android live workout screen`.

**Prompt:** Build a readable, touch-friendly Flutter screen for device status, power, cadence, speed, heart rate, timer, and bounded live charts. Match the portable semantics from Phase 3, including absent values and pause/resume behavior. Keep a training session active only while the app is foregrounded for this MVP, and communicate what happens if the app is backgrounded or killed. Use local mock/synthetic telemetry to develop UI without hardware, then validate with a trainer and HRM. Add recording/export only as a separate sub-issue if required for MVP; when added, preserve the Windows raw CSV meaning, use Android-appropriate app storage and an explicit export/share flow, and test stop/close flushing. Keep ERG control as a separate follow-on issue after safe protocol validation.

**Done when:** A user can scan, connect, start/pause/stop a workout, see live values/charts, recover from device loss, and end the session cleanly on a real phone. Any deferred recording or resistance control is visibly out of scope for this APK and listed as a follow-on issue.

## Phase 6 — Package and validate an APK

**Depends on:** Phase 5. **Issue title:** `Produce installable Android APK`.

**Prompt:** Check the current Flutter/Android build requirements and set app ID, display name, icon, minimum Android version, and release version deliberately. Run Flutter analysis/tests, build a release APK following Flutter's current Android release guide [7], install it on at least one physical phone, and repeat the MVP device and shutdown checks. For a distributable release, manage signing keys outside the repository and document a reproducible local signing process; Android requires signed APKs [8]. If Play distribution is chosen, assess app-bundle and Play requirements separately. Document exact output path, build command, supported phone architectures, and how to install/update the APK.

**Done when:** An installable release APK exists and passes real-device smoke tests. No signing secrets or personal device identifiers are committed. If only a debug APK can be produced, the phase remains open with a precise reason.

## Phase 7 — Refresh the Windows UI after behavior is stable

**Depends on:** Phases 1 and 2; can run independently of Android Phases 4–6. **Issue title:** `Improve Windows workout UI and resizing`.

**Prompt:** First sketch a desktop layout for the existing flows: prominent live metrics/timer, clear connection and recording states, accessible ERG controls, and charts that resize. Then improve WinForms incrementally using layout containers, DPI-aware sizing, readable typography/colors, keyboard navigation, and accessible status labels. Keep the existing `TimeSeriesChart` data cap and time window. Consider .NET 10 WinForms theming only after verifying its behavior with the custom chart [9]. Do not migrate the Windows app to Flutter in this issue. If a unified Flutter Windows UI is later desired, create a separate feasibility issue covering replacement of Windows BLE, packaging, and feature parity before committing to a rewrite.

**Done when:** The desktop app works at common window sizes and DPI settings, device errors are easy to see, existing trainer/HRM/recording flows still work, and the build and hardware smoke checks are recorded.

## Follow-on issues after the first APK

- **Android ERG control:** Verify the target trainer's actual Tacx FE-C-over-BLE or FTMS GATT command and status sequence on Android, enforce safe bounds and single in-flight command, then test modest watt targets while pedaling. An acknowledged command alone does not prove physical resistance changed.
- **Android raw/workout recording:** Decide whether the app needs raw diagnostic CSV, processed workout history, or both. Version any new workout format, provide export, and preserve existing Windows recordings.
- **Background workout support:** Only if needed, design notification, foreground service or companion-device behavior against current Android rules [6], then test screen lock, app switch, and process death.
- **Simulation slope control:** Extend the current session builder only after the exact BLE command, response, supported gradient range, and behavior on the original NEO Smart have been verified. Do not replace gradients with watt targets.
- **Flutter Windows feasibility:** Revisit only if the WinForms UI refresh is insufficient and Windows BLE, packaging, and feature-parity constraints can be met.

## Sources to recheck when executing phases

1. [.NET support policy](https://dotnet.microsoft.com/en-us/platform/support/policy).
2. [Microsoft WinForms upgrade guide](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/migration/).
3. [.NET 10 breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/10) and [.NET 9 breaking changes](https://learn.microsoft.com/en-us/dotnet/core/compatibility/9).
4. [Flutter supported deployment platforms](https://docs.flutter.dev/reference/supported-platforms) and [platform integration](https://docs.flutter.dev/platform-integration/).
5. [Android Bluetooth permissions](https://developer.android.com/develop/connectivity/bluetooth/bt-permissions).
6. [Android BLE background communication](https://developer.android.com/develop/connectivity/bluetooth/ble/background).
7. [Flutter Android release guide](https://docs.flutter.dev/deployment/android).
8. [Android app signing](https://developer.android.com/studio/publish/app-signing).
9. [WinForms in .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/whats-new/net100).
