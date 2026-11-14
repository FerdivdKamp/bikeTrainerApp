# Android MVP

This Android-only .NET application reuses the existing sensor and session code.
It has Connect, Test, Create and Session pages. Keep the app open during a ride;
background workouts are not supported, and leaving the app pauses the clock.

Install the .NET 10 Android workload, Android SDK and Java dependencies, then
build with `dotnet build .\Android\ErgTrainer.Android.csproj`. For a standalone
installable APK, publish a signed release build. Connect the phone for debugging
or transfer the signed APK to it.

On the phone, grant Nearby devices permission on the Connect page, scan and
select each device. On Test, try the four-beep sound alongside Spotify or test
an ERG target with a connected Tacx FE-C-over-BLE trainer.

On Create, enter a name and add blocks with durations in seconds and watt
targets. A duration can be a whole number or a product such as `60*5` for
300 seconds. Tap a block to select it; duplicate it or hold and drag it onto
another block to change the order. Save the workout locally or export a copy through Android's file picker;
Google Drive can be selected if its document provider is available. Open saved
workouts on Create or Session. Each local save creates a new copy.

On Session, start a valid ERG workout after connecting a trainer that supports
Tacx FE-C-over-BLE control. The first target must be accepted before the clock
starts. Pause keeps the current target; stopping, finishing or disconnecting can
also leave the last target in effect. The app stops on trainer disconnection
or a rejected block command. It does not yet reconnect during a workout.

Each ride saves a workout snapshot and records timestamped telemetry in a CSV,
flushed approximately once per second. Saved ride filenames are listed on the
Session page and can be exported to a document provider. Workouts and rides are
kept in app-private storage: uninstalling the app or clearing its data removes
them, so export copies you want to keep. Export is user initiated, not automatic
cloud sync. Session audio cues, simulation workouts and background operation
are not implemented.

BLE control, recording, export and the sound mix must be verified on the physical
phone, trainer and heart-rate monitor. Begin with modest targets and check the
actual resistance: an accepted command alone does not prove it changed.

For USB debugging, enable Developer options and USB debugging on the phone,
connect it to the computer and approve the debugging prompt. Android's
`adb logcat` records crashes and FE-C command/status diagnostics under the
`ErgTrainer` tag. These diagnostics include trainer protocol packets; avoid
sharing unreviewed logs publicly.
