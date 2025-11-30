// ERG Trainer + HRM — Minimal .NET 8 WinForms App (slightly larger window)
// Enlarged window size for better visibility.

using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using SmallEarthTech.AntPlus;
using SmallEarthTech.AntRadioInterface;
using SmallEarthTech.AntUsbStick;

internal static class Program
{
    [STAThread]
    static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new MainForm());
    }
}

public class MainForm : Form
{
    private readonly Button _btnConnect = new() { Text = "Connect", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };
    private readonly Button _btnDisconnect = new() { Text = "Disconnect", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Enabled = false };
    private readonly NumericUpDown _numErg = new() { Minimum = 50, Maximum = 1000, Value = 200, Increment = 5, Width = 100 };
    private readonly Button _btnSetErg = new() { Text = "Set ERG", AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink };

    private readonly Label _lblTrainer = new() { AutoSize = true, Text = "Trainer: (disconnected)" };
    private readonly Label _lblPower = new() { AutoSize = true, Text = "Power: — W" };
    private readonly Label _lblCad = new() { AutoSize = true, Text = "Cadence: — rpm" };
    private readonly Label _lblSpeed = new() { AutoSize = true, Text = "Speed: — kph" };
    private readonly Label _lblHrm = new() { AutoSize = true, Text = "HR: — bpm" };

    private ITrainerController _trainer;
    private IHeartRateSensor _hrm;
    private IHost? _antHost;
    private AntDeviceCollection _devices;

    public MainForm()
    {
        Text = "ERG + HRM Minimal";
        Size = new Size(1080, 800);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;

        _antHost = Host.CreateDefaultBuilder()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IAntRadio, AntRadio>();    // From AntUsbStick
                services.AddSingleton<AntDeviceCollection>();    // From AntPlus
            })
            .Build();

        _devices = _antHost.Services.GetRequiredService<AntDeviceCollection>();

        _trainer = new FakeTrainer();
        _hrm = new AntPlusHeartRate(_devices);

        var flow = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, Padding = new Padding(30), AutoScroll = true, WrapContents = false, AutoSize = false };
        var row1 = Row(_btnConnect, _btnDisconnect);
        var row2 = Row(new Label { Text = "Target (ERG, W):", AutoSize = true }, _numErg, _btnSetErg);
        var row3 = Row(_lblTrainer);
        var row4 = Row(_lblPower, _lblCad, _lblSpeed, _lblHrm);

        flow.Controls.Add(row1);
        flow.Controls.Add(row2);
        flow.Controls.Add(row3);
        flow.Controls.Add(row4);
        Controls.Add(flow);

        _btnConnect.Click += async (s, e) => await ConnectAsync();
        _btnDisconnect.Click += async (s, e) => await DisconnectAsync();
        _btnSetErg.Click += async (s, e) => await SetErgAsync((int)_numErg.Value);

        _trainer.DataUpdated += (_, data) => BeginInvoke(() => UpdateTrainerData(data));
        _trainer.ConnectionChanged += (_, connected) => BeginInvoke(() => OnTrainerConnectionChanged(connected));
        _hrm.HeartRateUpdated += (_, bpm) => BeginInvoke(() => _lblHrm.Text = $"HR: {bpm} bpm");
    }

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var row = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 0, 0, 12)
        };
        foreach (var c in controls)
        {
            c.Margin = new Padding(0, 0, 12, 0);
            row.Controls.Add(c);
        }
        return row;
    }
    private async Task ConnectAsync()
    {
        try
        {
            _btnConnect.Enabled = false;
            _lblTrainer.Text = "Trainer: connecting...";

            await _trainer.ConnectAsync();
            await _hrm.ConnectAsync();

            _btnDisconnect.Enabled = true;
            _btnSetErg.Enabled = true;
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Connect error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _btnConnect.Enabled = true;
        }
    }

    private async Task DisconnectAsync()
    {
        await _trainer.DisconnectAsync();
        await _hrm.DisconnectAsync();
        _btnConnect.Enabled = true;
        _btnDisconnect.Enabled = false;
        _btnSetErg.Enabled = false;
        _lblTrainer.Text = "Trainer: (disconnected)";
        _lblPower.Text = "Power: — W";
        _lblCad.Text = "Cadence: — rpm";
        _lblSpeed.Text = "Speed: — kph";
        _lblHrm.Text = "HR: — bpm";
    }

    private async Task SetErgAsync(int watts)
    {
        try
        {
            await _trainer.SetErgTargetPowerAsync(watts);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "ERG error", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void UpdateTrainerData(TrainerData d)
    {
        _lblTrainer.Text = d.Connected ? $"Trainer: {d.DeviceName}" : "Trainer: (disconnected)";
        _lblPower.Text = $"Power: {d.PowerWatts:F0} W";
        _lblCad.Text = $"Cadence: {d.CadenceRpm:F0} rpm";
        _lblSpeed.Text = $"Speed: {d.SpeedKph:F1} kph";
    }

    private void OnTrainerConnectionChanged(bool connected)
    {
        _btnDisconnect.Enabled = connected;
        _btnSetErg.Enabled = connected;
    }
}

public record TrainerData(bool Connected, string DeviceName, double PowerWatts, double CadenceRpm, double SpeedKph);

public interface ITrainerController
{
    event EventHandler<bool> ConnectionChanged;
    event EventHandler<TrainerData> DataUpdated;

    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();

    Task RequestControlAsync(CancellationToken ct = default);
    Task SetErgTargetPowerAsync(int watts, CancellationToken ct = default);
}

public interface IHeartRateSensor
{
    event EventHandler<int> HeartRateUpdated;
    Task ConnectAsync(CancellationToken ct = default);
    Task DisconnectAsync();
}

public sealed class FakeTrainer : ITrainerController
{
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<TrainerData>? DataUpdated;

    private readonly System.Timers.Timer _timer = new(1000);
    private bool _connected;
    private int _targetW = 200;
    private Random _rng = new();

    public FakeTrainer()
    {
        _timer.Elapsed += (_, __) =>
        {
            if (!_connected) return;
            var p = _targetW + _rng.Next(-10, 11);
            var cad = 85 + _rng.Next(-5, 6);
            var spd = 35 + _rng.Next(-2, 3) * 0.5;
            DataUpdated?.Invoke(this, new TrainerData(true, "Fake NEO (sim)", p, cad, spd));
        };
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        _connected = true;
        _timer.Start();
        ConnectionChanged?.Invoke(this, true);
        DataUpdated?.Invoke(this, new TrainerData(true, "Fake NEO (sim)", 0, 0, 0));
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _timer.Stop();
        _connected = false;
        ConnectionChanged?.Invoke(this, false);
        DataUpdated?.Invoke(this, new TrainerData(false, "", 0, 0, 0));
        return Task.CompletedTask;
    }

    public Task RequestControlAsync(CancellationToken ct = default) => Task.CompletedTask;

    public Task SetErgTargetPowerAsync(int watts, CancellationToken ct = default)
    {
        _targetW = Math.Clamp(watts, 50, 1000);
        return Task.CompletedTask;
    }
}

public sealed class FakeHeartRateSensor : IHeartRateSensor
{
    public event EventHandler<int>? HeartRateUpdated;
    private System.Timers.Timer _timer = new(1000);
    private Random _rng = new();

    public FakeHeartRateSensor()
    {
        _timer.Elapsed += (_, __) => HeartRateUpdated?.Invoke(this, 130 + _rng.Next(-5, 6));
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        _timer.Start();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        _timer.Stop();
        return Task.CompletedTask;
    }
}
