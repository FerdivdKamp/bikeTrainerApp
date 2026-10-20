# Prompt: Training session builder for ErgTrainer

Implement a structured indoor-cycling training session builder in this repository.
Follow `AGENTS.md` before editing. Keep the .NET 8 WinForms target, x86 platform,
existing BLE telemetry, Tacx FE-C over BLE ERG control, FTMS test, ANT+ heart-rate
code, recordings, and the user's local files intact. Do not commit or push.

## Product shape

Add a `TabControl` to `MainForm`: the existing live trainer screen becomes one
tab, and **Session Builder** becomes a second tab in the same application. Keep
device connection state shared so switching tabs does not disconnect the trainer.
Make the layout usable at the current window size without hiding existing
controls. Put editing UI in a focused reusable WinForms control; keep the
session model and file conversion independent of WinForms and hardware APIs.

The first version should build and run simple ERG workouts. A session has a
name, optional description, and an ordered list of steps. Each step has a name,
positive duration in seconds, and a fixed target power in watts within the
trainer's supported app range (currently 0–1000 W). In the builder, let the
user add, edit, duplicate, reorder, and remove steps. Show total duration and
a simple power-over-time preview. Validate empty names, durations, power
limits, and total length before saving or starting. Avoid silent clamping.

## Workout files

Use `.erg` as the first import/export format because it records absolute watts
over elapsed time and can be imported by several indoor-training products.
Use the documented `[COURSE HEADER]` / `[COURSE DATA]` structure with
`MINUTES WATTS`, invariant-culture numbers, and paired points at boundaries
where power changes. Parse and write durations without losing seconds to
decimal-minute rounding. Preserve a session's name and description when the
format allows. Make unsupported ramps, text cues, malformed sections, and
out-of-range values visible to the user; do not silently reinterpret them as
fixed steps. A Save dialog must not overwrite an existing workout without
normal WinForms confirmation. Use a chosen local path; do not touch recording
files or assume a Zwift installation.

Design the in-memory model so a later `.zwo` import/export can convert fixed
watts to/from fractions of FTP with an explicit FTP value. Do not label `.erg`
as a universal standard. Keep `.fit` activity recordings distinct from `.fit`
workout prescriptions.

Format and compatibility references:

- [TrainerRoad: ERG and MRC format and import](https://support.trainerroad.com/hc/trainerroad-support/articles/201944204-creating-a-workout-from-an-erg-or-mrc-file)
- [GoldenCheetah ERG example](https://github.com/GoldenCheetah/GoldenCheetah/blob/master/test/workouts/RampTest.erg)
- [TrainingPeaks structured workout export formats](https://help.trainingpeaks.com/hc/en-us/articles/115000325647-Structured-Workout-sync-and-Manual-Export)
- [Zwift custom workouts and ZWO import](https://support.zwift.com/it/custom-workouts-ryGOTVEPs)
- [Garmin FIT workout file definition](https://developer.garmin.com/fit/file-types/workout/)

## Running a session

Let the user start a validated session only when a compatible ERG control path
is connected. Use the existing `BleTacxTrainer.SetTargetPowerAsync` for the
original NEO Smart; do not send FTMS or guessed commands to a trainer that
only exposes the current FTMS test action. At each step boundary, send that
step's target once and wait for the command result before treating the step as
active. Show current step, target, remaining step time, total remaining time,
and command status. Handle pause, resume, stop, disconnect, and form close so
the session cannot continue sending targets after it ends. Define and show a
safe stop behavior for the NEO, such as explicitly sending a supported low
target before disconnect, and do not assume that stopping a UI timer releases
trainer resistance. Keep the UI responsive and prevent overlapping control
writes. Report rejected commands and timeouts without crashing the app.

Keep the existing free-ride timer/chart behavior clear: either integrate the
session clock with it or make the two modes mutually exclusive, with obvious
UI state. Use a monotonic clock for step boundaries; pausing must not consume
step time. Keep elapsed-time calculations and step transitions testable without
Bluetooth or WinForms.

## Validation

Inspect `MainForm.cs`, `Controls/TrainingTimerControl.cs`,
`Sensors/BleTacxTrainer.cs`, and their event/disconnect paths first. Add
deterministic tests for ERG parsing/serialization and step timing if a small
test project is justified; do not add hardware-dependent tests. At minimum,
verify an ERG round trip, malformed files, exact step boundaries, pause/resume,
and cancellation/disconnect behavior. Run `dotnet build .\ergtrainer.sln`.
Report the changed files, build results, format limitations, and concise manual
steps to test a short session on the NEO Smart. State which trainer behavior
could not be verified without physical hardware.
