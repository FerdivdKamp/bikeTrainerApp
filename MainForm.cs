using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using ErgTrainer.Sensors;

namespace ErgTrainer
{
    public class MainForm : Form
    {
        private readonly IHeartRateSensor _bleSensor;

        private readonly Button _btnConnectBle;
        private readonly Button _btnDisconnectBle;
        private readonly Label _lblHeartRate;
        private readonly Label _lblStatus;

        public MainForm()
        {
            Text = "ergtrainer – BLE HRM";
            Width = 400;
            Height = 180;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            _btnConnectBle = new Button
            {
                Text = "Connect BLE",
                Left = 20,
                Top = 20,
                Width = 120
            };

            _btnDisconnectBle = new Button
            {
                Text = "Disconnect BLE",
                Left = 160,
                Top = 20,
                Width = 120
            };

            _lblHeartRate = new Label
            {
                Text = "HR: -- bpm",
                Left = 20,
                Top = 70,
                AutoSize = true
            };

            _lblStatus = new Label
            {
                Text = "Status: idle",
                Left = 20,
                Top = 100,
                AutoSize = true
            };

            Controls.AddRange(new Control[]
            {
                _btnConnectBle,
                _btnDisconnectBle,
                _lblHeartRate,
                _lblStatus
            });

            // Only BLE for now – no ANT dongle required
            _bleSensor = new BleHeartRateSensor();
            _bleSensor.HeartRateReceived += BleSensor_HeartRateReceived;

            _btnConnectBle.Click += BtnConnectBle_Click;
            _btnDisconnectBle.Click += BtnDisconnectBle_Click;
        }

        private async void BtnConnectBle_Click(object? sender, EventArgs e)
        {
            _lblStatus.Text = "Status: connecting...";
            bool ok = await _bleSensor.ConnectAsync();
            _lblStatus.Text = ok ? "Status: BLE connected" : "Status: BLE not found";
        }

        private async void BtnDisconnectBle_Click(object? sender, EventArgs e)
        {
            _lblStatus.Text = "Status: disconnecting...";
            await _bleSensor.DisconnectAsync();
            _lblStatus.Text = "Status: BLE disconnected";
        }

        private void BleSensor_HeartRateReceived(object? sender, int bpm)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => BleSensor_HeartRateReceived(sender, bpm)));
                return;
            }

            _lblHeartRate.Text = $"HR: {bpm} bpm";
        }

        protected override async void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            await _bleSensor.DisconnectAsync();
        }
    }
}
