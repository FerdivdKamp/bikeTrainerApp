# Prompt: Bluetooth control for the original Tacx NEO Smart

Implement trainer resistance control over **Bluetooth Low Energy** for my original Tacx NEO Smart in this ErgTrainer repository. Do not use ANT+ for trainer control. Keep the existing ANT+ heart-rate code outside the scope of this task.

The app currently connects to the NEO Smart through the Cycling Power Service and receives telemetry, but its **Test FTMS control** button reports `FTMS control is unavailable on this connection.` The original NEO Smart uses Tacx's older Bluetooth control method rather than exposing the FTMS control point. Do not assume FTMS commands will work on this model.

Before editing, follow the project's AGENTS.md instructions supplied with the task, if available. Read `MainForm.cs`, `Sensors/BleTacxTrainer.cs`, and the current device event and disconnect paths. Confirm the control protocol and required GATT service, characteristic, command, response, and sequencing details from reliable documentation or verified protocol code. Garmin's [Tacx trainer communication guide](https://developer.garmin.com/tacx-trainer/overview/) distinguishes Tacx "FEC over BLE" from FTMS; use it as a starting point. If the command format is unavailable or uncertain, do not guess UUIDs or send experimental resistance commands. Document the exact missing information and implement only safe discovery or diagnostics that help obtain it.

For a verified protocol, make a small, reviewable implementation that:

1. Detects and reports the available Bluetooth control method when connecting. Preserve the existing Cycling Power telemetry path and FTMS support for trainers that expose it.
2. Requests control as required by the protocol, then lets the user set a target power in watts (ERG mode). Validate the input and keep commands within the trainer's documented range. Show a clear result or error, including when another app owns control.
3. Keeps protocol and GATT operations in `Sensors/` and UI state in `MainForm.cs`. Make commands asynchronous, honor cancellation where supported, prevent overlapping writes, and keep the WinForms UI responsive.
4. Unsubscribe from indications or notifications and release control and GATT resources on disconnect and form close. Handle lost connections, timeouts, unsupported commands, and malformed replies without crashing the app.
5. Replace or disable the misleading FTMS-only test button for this connection, while leaving FTMS testing available for actual FTMS trainers.

Avoid new packages unless the framework and existing `InTheHand.BluetoothLE` dependency cannot do the job. Preserve the .NET 8 Windows Forms target and x86 platform. Keep unrelated code and user files untouched. Do not commit, push, or create a branch.

Build with `dotnet build .\ergtrainer.sln`. Report what changed, the protocol evidence used, build results, and what remains unverified without physical hardware. Give concise manual steps to test connection, control acquisition, a modest target power, and clean disconnect on the NEO Smart.
