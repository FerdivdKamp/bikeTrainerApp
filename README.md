# ErgTrainer

ErgTrainer is a .NET 8 Windows Forms application for reading live data from a
BLE Tacx trainer and BLE heart-rate monitor. It displays decoded values and can
capture the original BLE characteristic payloads for offline interpretation.

## Build and run

The application targets Windows and x86.

    dotnet build .\ergtrainer.sln
    dotnet run --project .\ergtrainer.csproj

You can also open ergtrainer.sln in Visual Studio and start it with F5.

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
its status reports acceptance or rejection. Trainers exposing FTMS retain the
**Test FTMS control** action instead.

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

## Legacy ANT+ support (to be deprecated)

The current application flow uses Bluetooth for the trainer and HRM. The
repository still contains older ANT+ sensor code and package references, which
are not used for the Bluetooth test run and are expected to be removed later.
ANT+ requires a compatible USB ANT+ stick.

    dotnet add package SmallEarthTech.AntUsbStick
    dotnet add package SmallEarthTech.AntPlus
