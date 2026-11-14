using Android;
using Android.App;
using Android.Bluetooth;
using Android.Content.PM;
using Android.Media;
using Android.OS;
using Android.Views;
using Android.Widget;
using ErgTrainer.Sensors;
using InTheHand.Bluetooth;
using BluetoothDevice = InTheHand.Bluetooth.BluetoothDevice;
using OperationCanceledException = System.OperationCanceledException;

namespace ErgTrainer.AndroidApp;

[Activity(Label = "ErgTrainer", MainLauncher = true, Exported = true)]
public class MainActivity : Activity
{
    private readonly BleTacxTrainer trainer = new();
    private readonly BleHeartRateSensor heartRate = new();
    private readonly CancellationTokenSource lifetime = new();
    private IReadOnlyList<DeviceScanner.FoundDevice> devices = Array.Empty<DeviceScanner.FoundDevice>();
    private TextView status = null!;
    private TextView readings = null!;
    private Button scan = null!;
    private Button connectTrainer = null!;
    private Button connectHeartRate = null!;
    private Button sound = null!;
    private bool busy;
    private bool destroyed;

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        Window?.AddFlags(WindowManagerFlags.KeepScreenOn);
        var layout = new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
        layout.SetPadding(32, 32, 32, 32);
        status = new TextView(this) { Text = "Scan for your trainer and heart-rate monitor.", TextSize = 16 };
        readings = new TextView(this) { Text = "Power: -- W\nCadence: -- rpm\nSpeed: -- km/h\nHeart rate: -- bpm", TextSize = 22 };
        scan = MakeButton("Scan for devices", async () => await ScanAsync());
        connectTrainer = MakeButton("Choose trainer", () => ChooseDevice(true));
        connectHeartRate = MakeButton("Choose heart-rate monitor", () => ChooseDevice(false));
        sound = MakeButton("Test countdown sound", async () => await PlaySoundAsync());
        layout.AddView(status);
        layout.AddView(scan);
        layout.AddView(connectTrainer);
        layout.AddView(connectHeartRate);
        layout.AddView(readings);
        layout.AddView(sound);
        var scroll = new ScrollView(this);
        scroll.SetFitsSystemWindows(true);
        if (OperatingSystem.IsAndroidVersionAtLeast(30))
            scroll.SetOnApplyWindowInsetsListener(new SystemBarInsetsListener());
        scroll.AddView(layout);
        SetContentView(scroll);
        trainer.DataUpdated += TrainerDataUpdated;
        heartRate.HeartRateReceived += HeartRateReceived;
        UpdateButtons();
    }

    private sealed class SystemBarInsetsListener : Java.Lang.Object, View.IOnApplyWindowInsetsListener
    {
        public WindowInsets OnApplyWindowInsets(View? view, WindowInsets? insets)
        {
            if (view != null && insets != null && OperatingSystem.IsAndroidVersionAtLeast(30))
            {
                var bars = insets.GetInsets(WindowInsets.Type.SystemBars());
                if (bars != null)
                    view.SetPadding(bars.Left, bars.Top, bars.Right, bars.Bottom);
            }
            return insets!;
        }
    }

    private Button MakeButton(string title, Action action)
    {
        var button = new Button(this) { Text = title };
        button.Click += (_, _) => action();
        return button;
    }

    private void UpdateButtons()
    {
        scan.Enabled = !busy;
        connectTrainer.Enabled = !busy && devices.Count > 0;
        connectHeartRate.Enabled = !busy && devices.Count > 0;
    }

    private bool HasPermissions()
    {
        string[] required = OperatingSystem.IsAndroidVersionAtLeast(31)
            ? new[] { Manifest.Permission.BluetoothScan, Manifest.Permission.BluetoothConnect }
            : new[] { Manifest.Permission.AccessFineLocation };
        if (required.All(p => CheckSelfPermission(p) == Permission.Granted))
            return true;
        RequestPermissions(required, 1);
        status.Text = "Allow Bluetooth access, then tap Scan again.";
        return false;
    }

    private async Task ScanAsync()
    {
        if (!HasPermissions()) return;
        busy = true;
        UpdateButtons();
        status.Text = "Scanning…";
        try
        {
            var manager = GetSystemService(BluetoothService) as BluetoothManager
                ?? throw new InvalidOperationException("Bluetooth is unavailable.");
            devices = await DeviceScanner.ScanAsync(manager, lifetime.Token);
            if (!destroyed)
                status.Text = devices.Count == 0
                    ? "No devices found. Check Bluetooth and wake your sensors."
                    : $"Found {devices.Count} devices. Choose a trainer or heart-rate monitor.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!destroyed) status.Text = $"Scan failed: {ex.Message}"; }
        finally { busy = false; if (!destroyed) UpdateButtons(); }
    }

    private void ChooseDevice(bool isTrainer)
    {
        var choices = devices.ToArray();
        var names = choices.Select(d => $"{(string.IsNullOrWhiteSpace(d.Name) ? "Unnamed device" : d.Name)}\n{d.Id}").ToArray();
        var dialog = new AlertDialog.Builder(this);
        dialog.SetTitle(isTrainer ? "Choose trainer" : "Choose heart-rate monitor");
        dialog.SetItems(names, async (_, args) => await ConnectAsync(choices[args.Which].Device, isTrainer));
        dialog.SetNegativeButton("Cancel", (_, _) => { });
        dialog.Show();
    }

    private async Task ConnectAsync(BluetoothDevice device, bool isTrainer)
    {
        busy = true;
        UpdateButtons();
        status.Text = $"Connecting to {device.Name ?? "device"}…";
        try
        {
            if (isTrainer) await trainer.DisconnectAsync();
            else await heartRate.DisconnectAsync();
            bool connected = isTrainer
                ? await trainer.ConnectToDeviceAsync(device, lifetime.Token)
                : await heartRate.ConnectToDeviceAsync(device, lifetime.Token);
            if (!connected)
            {
                if (isTrainer) await trainer.DisconnectAsync();
                else await heartRate.DisconnectAsync();
            }
            if (!destroyed) status.Text = connected
                ? $"Connected {(isTrainer ? "trainer" : "heart-rate monitor")}: {device.Name}"
                : "Connection failed. Check that the correct device is awake and try again.";
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (!destroyed) status.Text = $"Connection failed: {ex.Message}";
            if (isTrainer) await trainer.DisconnectAsync();
            else await heartRate.DisconnectAsync();
        }
        finally { busy = false; if (!destroyed) UpdateButtons(); }
    }

    private string trainerReading = "Power: -- W\nCadence: -- rpm\nSpeed: -- km/h";
    private string heartReading = "Heart rate: -- bpm";

    private void TrainerDataUpdated(object? sender, TrainerData data)
    {
        if (destroyed) return;
        RunOnUiThread(() =>
        {
            if (destroyed) return;
            trainerReading = $"Power: {data.PowerWatts:F0} W\nCadence: {data.CadenceRpm?.ToString("F0") ?? "--"} rpm\nSpeed: {data.SpeedKph?.ToString("F1") ?? "--"} km/h";
            readings.Text = trainerReading + "\n" + heartReading;
        });
    }

    private void HeartRateReceived(object? sender, int bpm)
    {
        if (destroyed) return;
        RunOnUiThread(() =>
        {
            if (destroyed) return;
            heartReading = $"Heart rate: {bpm} bpm";
            readings.Text = trainerReading + "\n" + heartReading;
        });
    }

    private async Task PlaySoundAsync()
    {
        sound.Enabled = false;
        try
        {
            const int rate = 44100;
            const int durationMs = 150;
            const int intervalMs = 1000;
            int count = rate * (3 * intervalMs + durationMs) / 1000;
            var samples = new short[count];
            for (int beep = 0; beep < 4; beep++)
            {
                int start = beep * rate * intervalMs / 1000;
                int length = rate * durationMs / 1000;
                double frequency = beep == 3 ? 1100 : 700;
                for (int i = 0; i < length; i++)
                {
                    double envelope = Math.Min(1.0, Math.Min(i, length - 1 - i) / (rate * 0.01));
                    samples[start + i] = (short)(short.MaxValue * 0.35 * envelope * Math.Sin(2 * Math.PI * frequency * i / rate));
                }
            }
            using var attributes = new AudioAttributes.Builder()!
                .SetUsage(AudioUsageKind.Media)!
                .SetContentType(AudioContentType.Sonification)!
                .Build() ?? throw new InvalidOperationException("Could not configure audio attributes.");
            using var format = new AudioFormat.Builder()!
                .SetSampleRate(rate)!
                .SetChannelMask(ChannelOut.Mono)!
                .SetEncoding(Encoding.Pcm16bit)!
                .Build() ?? throw new InvalidOperationException("Could not configure audio format.");
            using var track = new AudioTrack.Builder()!
                .SetAudioAttributes(attributes)!
                .SetAudioFormat(format)!
                .SetBufferSizeInBytes(samples.Length * sizeof(short))!
                .SetTransferMode(AudioTrackMode.Static)!
                .Build()!;
            if (track.Write(samples, 0, samples.Length) != samples.Length)
                throw new InvalidOperationException("Could not load the sound.");
            track.Play();
            await Task.Delay(3300, lifetime.Token);
            track.Stop();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { if (!destroyed) status.Text = $"Sound test failed: {ex.Message}"; }
        finally { if (!destroyed) sound.Enabled = true; }
    }

    protected override async void OnDestroy()
    {
        destroyed = true;
        lifetime.Cancel();
        trainer.DataUpdated -= TrainerDataUpdated;
        heartRate.HeartRateReceived -= HeartRateReceived;
        try
        {
            await trainer.DisconnectAsync();
            await heartRate.DisconnectAsync();
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        finally { lifetime.Dispose(); }
        base.OnDestroy();
    }
}
