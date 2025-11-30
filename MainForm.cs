using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ErgTrainer.Sensors;
using InTheHand.Bluetooth;

namespace ErgTrainer
{
    public class MainForm : Form
    {
        private readonly BleHeartRateSensor _bleSensor;

        private readonly Button _btnScan;
        private readonly Button _btnDisconnectBle;
        private readonly ListBox _listDevices;
        private readonly Label _lblHeartRate;
        private readonly Label _lblStatus;

        private List<BluetoothDevice> _availableDevices = new List<BluetoothDevice>();

        public MainForm()
        {
            Text = "ergtrainer – BLE HRM";
            Width = 500;
            Height = 400;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            _btnScan = new Button
            {
                Text = "Scan for Devices",
                Left = 20,
                Top = 20,
                Width = 150
            };

            _listDevices = new ListBox
            {
                Left = 20,
                Top = 60,
                Width = 440,
                Height = 200
            };

            _btnDisconnectBle = new Button
            {
                Text = "Disconnect BLE",
                Left = 20,
                Top = 270,
                Width = 150,
                Enabled = false
            };

            _lblHeartRate = new Label
            {
                Text = "HR: -- bpm",
                Left = 20,
                Top = 310,
                AutoSize = true
            };

            _lblStatus = new Label
            {
                Text = "Status: idle",
                Left = 20,
                Top = 335,
                AutoSize = true
            };

            Controls.AddRange(new Control[]
            {
                _btnScan,
                _listDevices,
                _btnDisconnectBle,
                _lblHeartRate,
                _lblStatus
            });

            // Only BLE for now – no ANT dongle required
            _bleSensor = new BleHeartRateSensor();
            _bleSensor.HeartRateReceived += BleSensor_HeartRateReceived;

            _btnScan.Click += BtnScan_Click;
            _listDevices.DoubleClick += ListDevices_DoubleClick;
            _btnDisconnectBle.Click += BtnDisconnectBle_Click;
        }

        private async void BtnScan_Click(object? sender, EventArgs e)
        {
            _btnScan.Enabled = false;
            _lblStatus.Text = "Status: scanning...";
            _listDevices.Items.Clear();
            _availableDevices.Clear();

            try
            {
                var devices = await BluetoothDeviceScanner.ScanForDevicesAsync();
                _availableDevices = devices.ToList();

                foreach (var device in _availableDevices)
                {
                    var displayName = string.IsNullOrEmpty(device.Name) ? $"Unknown ({device.Id})" : device.Name;
                    _listDevices.Items.Add(displayName);
                }

                _lblStatus.Text = $"Status: found {_availableDevices.Count} device(s)";
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"Status: scan failed - {ex.Message}";
            }
            finally
            {
                _btnScan.Enabled = true;
            }
        }

        private async void ListDevices_DoubleClick(object? sender, EventArgs e)
        {
            if (_listDevices.SelectedIndex < 0 || _listDevices.SelectedIndex >= _availableDevices.Count)
                return;

            var selectedDevice = _availableDevices[_listDevices.SelectedIndex];
            await ConnectToDeviceAsync(selectedDevice);
        }

        private async Task ConnectToDeviceAsync(BluetoothDevice device)
        {
            _btnScan.Enabled = false;
            _listDevices.Enabled = false;
            _lblStatus.Text = "Status: checking if device is a heart rate monitor...";

            try
            {
                // First check if it's a heart rate monitor
                bool isHrm = await BleHeartRateSensor.IsHeartRateMonitorAsync(device);
                
                if (!isHrm)
                {
                    _lblStatus.Text = $"Status: {device.Name ?? "Device"} is not a heart rate monitor";
                    return;
                }

                // It's a heart rate monitor, connect to it
                _lblStatus.Text = "Status: connecting to heart rate monitor...";
                bool ok = await _bleSensor.ConnectToDeviceAsync(device);
                if (ok)
                {
                    _lblStatus.Text = $"Status: connected to {device.Name ?? "device"}";
                    _btnDisconnectBle.Enabled = true;
                    _lblHeartRate.Text = "HR: -- bpm"; // Reset display
                }
                else
                {
                    _lblStatus.Text = "Status: connection failed";
                }
            }
            catch (Exception ex)
            {
                _lblStatus.Text = $"Status: connection error - {ex.Message}";
            }
            finally
            {
                _btnScan.Enabled = true;
                _listDevices.Enabled = true;
            }
        }

        private async void BtnDisconnectBle_Click(object? sender, EventArgs e)
        {
            _lblStatus.Text = "Status: disconnecting...";
            await _bleSensor.DisconnectAsync();
            _lblStatus.Text = "Status: BLE disconnected";
            _btnDisconnectBle.Enabled = false;
            _lblHeartRate.Text = "HR: -- bpm";
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
