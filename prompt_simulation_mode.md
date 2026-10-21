# Implement Simulation sessions on the Tacx NEO over BLE

Use this as a phased implementation prompt. Each phase can be assigned to an agent or copied into a GitHub issue. Read `agents.md` and the affected source before editing. Keep changes focused and reviewable. Do not commit, push, create branches, or alter recordings unless asked.

## Current state

- The WinForms Session Builder edits, validates, saves, loads, and previews Simulation blocks with a gradient percentage. Hardware Start is disabled.
- A session has editable rider and bike weights in kilograms, saved as `riderWeightKg` and `bikeWeightKg`. Current defaults are 75 kg and 10 kg for older files. Treat these as estimates that the user can change, not measured values.
- `MainForm.cs` runs ERG sessions with a stopwatch-based clock, cancellation, block transitions, and command status. The Trainer tab also has a manual slope test with rider and bike weight inputs. `Sensors/BleTacxTrainer.cs` sends FE-C target-power page 49 for ERG; the slope test sends user configuration page 55, neutral wind page 50, and track resistance page 51, matching each command's status reply. The packet code is in `Sensors/FecSimulationPackets.cs`.
- The app is BLE-only and x64. It has no ANT+ dependency. Do not reintroduce ANT+ or change architecture for this feature.
- A BLE acknowledgement is evidence that a command was accepted, not that resistance physically changed. The original NEO Smart requires a real trainer test before Simulation can be declared working.

## Phase 1: Verify the protocol and write the command contract

**Issue title:** `Verify Tacx FE-C-over-BLE Simulation command sequence`

**Prompt:** Audit the existing manual slope test and FE-C GATT discovery, packet construction, write serialization, response matching, disconnect handling, and ERG status path in `Sensors/BleTacxTrainer.cs` and `Sensors/FecSimulationPackets.cs`. Consult the [Tacx FE-C-over-BLE guide](https://github.com/jedla22/BleTrainerControl/blob/master/How-to%20FE-C%20over%20BLE%20v1_0_0.pdf), [Tacx-compatible implementation](https://github.com/jedla22/BleTrainerControl/blob/master/BleTrainerControl/BTLETrainerManager.m), [pycycling Tacx implementation](https://zacharybull.com/pycycling/_modules/pycycling/tacx_trainer_control.html), and underlying FE-C page definitions. Document field units, scaling, byte order, checksum, and command-status correlation. Review the manual test's estimated wheel diameter, fixed 1.98:1 gear ratio, neutral wind, and rolling-resistance settings against the original NEO Smart. Confirm whether user configuration and wind setup must be sent once at start or after reconnect, and whether track resistance activates Simulation mode. Record supported grades and how to handle values outside them. Separate documented behavior from assumptions and trainer observations. Do not enable Simulation Start in this phase.

**Done when:** The command sequence and response contract can be reviewed against sources, including rider and bike weight conversion and how a status response is matched to the command that produced it. Any unresolved device-specific detail is listed for physical testing.

## Phase 2: Implement and verify trainer commands

**Depends on:** Phase 1. **Issue title:** `Add safe BLE Simulation commands to Tacx trainer`

**Prompt:** Refine the manual slope test's user-configuration, wind, and track-resistance commands based on Phase 1. Keep the existing control gate so only one FE-C command is in flight. Validate inputs according to the documented contract. Match command-status page 71 by the requested FE-C page. Check timeout, cancellation, rejection, disconnect, and write failure without leaving a pending command or blocked gate. Keep FTMS separate; do not send Tacx FE-C packets to an FTMS-only trainer. Extend the existing deterministic packet and response tests where the audit reveals a gap. Test modest grades using the Trainer tab's manual button while pedaling.

**Done when:** Packet tests prove units, boundary values, checksum, and response matching; ERG still works; the solution builds without new warnings. Document the result of modest grade tests on the original NEO Smart while pedaling, including observed resistance changes, or explicitly leave hardware execution unavailable until that result exists.

## Phase 3: Run Simulation sessions

**Depends on:** Phase 2 and successful physical command validation. **Issue title:** `Enable Simulation workout execution`

**Prompt:** Extend the existing `MainForm` session runner and `SessionBuilderControl` to start a Simulation session only when a compatible FE-C-over-BLE trainer is connected. At start, send the session's rider and bike weights as user configuration, any required wind/rolling settings, then the first gradient. Start the session clock only after the first gradient is accepted. At each block boundary, send that block's gradient; keep current block display, instructions, charts, and pause/resume semantics. Define and explain what Pause and Stop do to trainer resistance, and handle final block, manual ERG controls, disconnect, cancellation, and reconnect without silently leaving a surprising slope active. Keep the run state accurate if a command fails. Do not turn gradients into watt targets or add a software physics approximation. Preserve ERG behavior.

**Done when:** Simulation Start is enabled only for a verified compatible trainer; slope changes follow block boundaries; errors stop or pause safely with visible status; ERG sessions and free ride still work.

## Phase 4: Validate the end-to-end workout

**Depends on:** Phase 3. **Issue title:** `Validate Simulation workouts on trainer`

**Prompt:** Run `dotnet build .\ergtrainer.sln` and `dotnet run --project .\Tests\SessionChecks.csproj`. On an original NEO Smart, try a short session with flat, modest uphill, and modest downhill blocks at a controlled cadence. Check the physical resistance response, timing, pause/resume, stop, disconnect during a block, and ERG after Simulation. Repeat with different rider/bike weights to verify that the configuration is applied; do not use extreme slopes for initial validation. Save and reload a new Simulation file and an older file without weight fields. Record trainer model/firmware and outcomes without committing identifiers or personal data. Update README with actual supported behavior and remaining limitations.

**Done when:** Build and deterministic checks pass, saved weights round-trip, older sessions still load, and a documented physical test confirms that gradient and weight changes affect the trainer as expected. If hardware is unavailable or behavior differs, leave Start disabled and report the exact gap.

## Implementation boundaries

- Weight belongs to the session, not each block; gradient belongs to each block. Existing JSON schema version 1 remains valid because older files receive the documented defaults.
- Keep all GATT and FE-C details in `Sensors/`; keep session timing and UI state in `MainForm` and controls.
- Respect the existing WinForms UI-thread and cleanup rules in `agents.md`.
- Preserve the user's other local edits and recordings.
