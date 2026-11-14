# ErgTrainer

ErgTrainer is a .NET 8 Windows Forms application for reading live data from a
BLE Tacx trainer and BLE heart-rate monitor. It displays decoded values and can
capture the original BLE characteristic payloads for offline interpretation.

## Build and run

The application targets Windows and x64. Trainer and heart-rate connections use BLE.

    dotnet build .\ergtrainer.sln
    dotnet run --project .\ergtrainer.csproj

You can also open ergtrainer.sln in Visual Studio and start it with F5.

## Training sessions

The **Trainer** tab keeps the existing live BLE interface and free-ride timer.
The **Session Builder** tab lets you create ERG or Simulation workouts, add and
reorder blocks, enter coaching instructions, preview progress, and save or load
versioned `.ergsession.json` files. Sessions are local files you choose with the
Save/Load dialogs. The builder accepts gradients from -20% to 20% for planning.

To run an ERG session, connect a Tacx trainer that reports **Tacx FE-C over BLE**
control, stop the free-ride timer, and select **Start session**. The app sends
the first watt target before starting the session clock, then sends each new
target at its block boundary. Pause freezes the clock and visual progress but
leaves the current trainer target in effect. Stopping or finishing also leaves
the last target in effect until you change it or disconnect the trainer.
If the trainer connection drops during a session, the clock pauses and the app
tries to reconnect up to three times. After reconnecting, it resends the current
ERG target and waits for you to press **Resume**. If automatic attempts fail,
use **Retry trainer** in the Trainer tab or **Stop** in the Session Builder.
An intentional disconnect stops the session. Trainer commands and resistance
should be checked on physical hardware before relying on a workout.

Starting a session automatically creates a small event CSV under
`Documents/ErgTrainer/SessionLogs`. It records timestamps for session starts,
block commands, pauses, disconnects, and reconnect attempts. It does not record
every telemetry sample or raw BLE packet. The **Record raw device signals**
checkbox is optional and intended for protocol/telemetry investigation, not
required to run or log a training session.

An ERG session also records an activity under `Documents/ErgTrainer/Activities`,
independent of the raw-signal checkbox. The `.jsonl` journal contains the UTC
start and end, block changes, pause/disconnect events, and measured power,
cadence, speed, and heart rate at up to one sample per second. Missing readings
stay missing. When the session completes or you stop it, the app exports a
matching `.fit` **Activity** file for Garmin Connect. The Trainer tab shows the
file path and has an **Open activity folder** button. The FIT file stores elapsed
time separately from active timer time; it contains no GPS track, estimated
distance, calories, or invented readings. FIT timestamps have one-second
resolution, so subsecond timing is retained in the journal. A session with no
measured telemetry keeps its journal but cannot produce a valid FIT record.

To import, open [Garmin Connect Web](https://connect.garmin.com/), choose
**Upload or Import Data**, and select the `.fit` file. This is a manual import;
the app does not sign in to Garmin or upload automatically. A failed FIT export
leaves the journal in place. If the app exits unexpectedly, recent journal rows
that reached the writer remain readable, but the journal may have no final end
event and no importable FIT file. The app does not reconstruct missing telemetry
from old event-only CSVs.

Simulation sessions can be edited, saved, loaded, and previewed. Their **Start**
button remains disabled until simulation blocks are wired to trainer control;
it does not substitute watt targets for gradients. The Tacx
[communication overview](https://developer.garmin.com/tacx-trainer/overview/)
distinguishes FE-C over BLE from FTMS, and a
[Tacx-compatible demonstration](https://github.com/jedla22/BleTrainerControl)
describes track resistance. A rider test reported distinct resistance at 2%,
3%, and 5% in the manual slope test.
Simulation sessions also store editable rider and bike weight estimates in kg.
Older session files without these fields load with 75 kg and 10 kg defaults;
update them to your own estimates before eventual hardware use. See
[`prompt_simulation_mode.md`](prompt_simulation_mode.md) for the implementation phases.

The hardware-independent session checks run with:

    dotnet run --project .\Tests\SessionChecks.csproj

## Trainer cadence and estimated speed

When the trainer provides Cycling Speed and Cadence (CSC) notifications, cadence
comes from crank revolutions and speed comes from wheel revolutions. The wheel
speed is an estimate based on the **Wheel mm** value in the Tacx Trainer panel.
It defaults to 2105 mm, matching the value used in the recording notebook; you
can change it while the app is running. A direct-drive trainer may report a
virtual wheel, so choose a circumference that gives a useful estimate for your
setup. Cadence or speed shows `--` until enough CSC events have arrived, or when
the corresponding CSC field is unavailable.

## Bluetooth ERG control

For an original Tacx NEO Smart that exposes Tacx FE-C over BLE, connect the
trainer, enter a target in **ERG watts** (0 to 1000 W), and select **Set ERG
target**. The status reports whether the trainer accepted the command. Start
with a modest target such as 150 W and pedal to check the actual resistance;
an accepted command alone does not verify the physical result. Disconnect the
trainer to release the BLE connection and control. Close other trainer apps if
the command is rejected or no status arrives.

The legacy command follows the [Tacx/Kinomap BleTrainerControl GitHub example](https://github.com/jedla22/BleTrainerControl)
(see its [control implementation](https://github.com/jedla22/BleTrainerControl/blob/master/BleTrainerControl/BTLETrainerManager.m)
and [FE-C over BLE guide](https://github.com/jedla22/BleTrainerControl/blob/master/How-to%20FE-C%20over%20BLE%20v1_0_0.pdf)):
service `6e40fec1`, notifications `6e40fec2`, writes `6e40fec3`, target-power
page 49 and command-status page 71. There is no separate request-control opcode
in this legacy example; the target-power command requests a mode change and
the app requests page 71 to check acceptance or rejection. Trainers exposing FTMS retain the
**Test FTMS control** action instead.

## Manual slope test

With a Tacx FE-C-over-BLE trainer connected, use **Slope %** (-20% to 20%),
**Rider kg**, and **Bike kg** in the Trainer tab, then select **Set test slope**.
The test sends
FE-C user configuration, neutral wind, and track resistance in order. It uses
the displayed Wheel mm circumference to estimate wheel diameter, a fixed 1.98:1
gear ratio, zero wind drag, and a 0.004 rolling-resistance coefficient. These
are test assumptions, not a calibrated riding model. The control status shows
which step was rejected or whether the slope command was accepted. Disconnecting
the trainer cancels an in-progress test.

For a first physical check, pedal in the same gear at a steady cadence and set
0%, then 3%, then 0% again. Compare how resistance feels and how power changes.
Repeat with a different rider-weight estimate if the grade change works. The
test control is limited to -5% through 5%. Set 0% to return to flat resistance,
or disconnect when finished. An accepted command is not proof that physical
resistance changed. The Session Builder will remain disabled for Simulation
execution until that is verified on the trainer.

## Raw signal recording

Raw recording preserves the incoming BLE payload before the application attempts
to interpret it. This makes it possible to verify the trainer's cadence, power,
and speed fields, and the HRM's heart-rate fields offline.

1. Start the app and select **Scan for Devices**.
2. Double-click the desired Bluetooth trainer or heart-rate monitor to connect.
3. Check **Record raw device signals** in the Bluetooth Devices panel.
4. Perform the test activity.
5. Uncheck **Record raw device signals** when finished, or close the app. This
   flushes and closes the recording file.

Each recording is stored in:

    Documents\ErgTrainer\Recordings

The file name follows raw-signals-YYYYMMDD-HHMMSSfff.csv. Recording can be
enabled before connecting a device; rows begin as soon as a connected device
sends data.

### CSV format

| Column | Meaning |
| --- | --- |
| timestamp_utc | When the application received the packet, in UTC. |
| source | trainer or heart-rate-monitor. |
| device | Bluetooth device name reported by Windows. |
| delivery_method | notification for a pushed BLE update, or poll for a read performed by the app. |
| service_uuid | BLE service that supplied the data. |
| characteristic_uuid | BLE characteristic that supplied the data. |
| raw_hex | Complete payload as hexadecimal bytes, before parsing. |

Notification and polling can produce similar rows. Keep both: the
delivery_method column distinguishes the device's pushed stream from the
application's polling fallback.

## Garmin HRM test run

1. Put on the HRM, moisten the electrode pads, and wait for it to become active.
2. Start ErgTrainer, scan, and double-click the Garmin HRM entry.
3. Confirm the status says it is connected.
4. Enable **Record raw device signals** and wait at least one minute. A few
   short bursts of activity are useful because they produce changes in heart
   rate.
5. Disable recording and open the newest CSV in
   Documents\ErgTrainer\Recordings.

Expect rows whose source is heart-rate-monitor. Retain the CSV even if the
displayed BPM looks wrong: the raw payload is what we need to verify the parser.
The HRM can be tested without a Tacx trainer connection.

When sharing a result for analysis, provide the CSV and note approximately when
you started moving and what BPM you expected. This gives us a reference point
for interpreting the packet flags and value bytes.
