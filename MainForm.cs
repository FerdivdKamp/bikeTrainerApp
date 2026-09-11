using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using ErgTrainer.Controls;
using ErgTrainer.Recorders;
using ErgTrainer.Sensors;
using InTheHand.Bluetooth;

namespace ErgTrainer
{
    public class MainForm : Form
    {
        private readonly BleTacxTrainer _tacxTrainer;
        private readonly BleHeartRateSensor _hrmSensor;
        private readonly SignalRecorder _signalRecorder;

        // Shared Device List
        private readonly GroupBox _grpDevices;
        private readonly Button _btnScan;
        private readonly ListBox _listDevices;
        private readonly CheckBox _chkRecordSignals;
        private readonly Label _lblRecordingStatus;
        private bool _changingRecordingState;

        // Tacx Trainer Section
        private readonly GroupBox _grpTacx;
        private readonly Button _btnDisconnectTacx;
        private readonly Label _lblTacxStatus;
        private readonly Label _lblTacxData;

        // HRM Section
        private readonly GroupBox _grpHrm;
        private readonly Button _btnDisconnectHrm;
        private readonly Label _lblHrmStatus;
        private readonly Label _lblHeartRate;

        // Charts Section
        private readonly GroupBox _grpCharts;
        private readonly TimeSeriesChart _chartPower;
        private readonly TimeSeriesChart _chartCadence;
        private readonly TimeSeriesChart _chartSpeed;
        private readonly TimeSeriesChart _chartHeartRate;

        // Timer Section
        private readonly GroupBox _grpTimer;
        private readonly TrainingTimerControl _trainingTimer;

        private List<BluetoothDevice> _availableDevices = new List<BluetoothDevice>();

        public MainForm()
        {
            Text = "ergtrainer – Tacx Trainer + HRM";
            Width = 1400;
            Height = 1060;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;

            // Initialize sensors
            _tacxTrainer = new BleTacxTrainer();
            _tacxTrainer.DataUpdated += TacxTrainer_DataUpdated;
            _tacxTrainer.RawDataReceived += Device_RawDataReceived;

            _hrmSensor = new BleHeartRateSensor();
            _hrmSensor.HeartRateReceived += HrmSensor_HeartRateReceived;
            _hrmSensor.RawDataReceived += Device_RawDataReceived;
            _signalRecorder = new SignalRecorder();

            // Shared Device List Section
            _grpDevices = new GroupBox
            {
                Text = "Bluetooth Devices",
                Left = 20,
                Top = 20,
                Width = 300,
                Height = 490
            };

            _btnScan = new Button
            {
                Text = "Scan for Devices",
                Left = 10,
                Top = 25,
                Width = 150
            };

            _listDevices = new ListBox
            {
                Left = 10,
                Top = 55,
                Width = 280,
                Height = 350
            };

            _chkRecordSignals = new CheckBox
            {
                Text = "Record raw device signals",
                Left = 10,
                Top = 415,
                AutoSize = true
            };

            _lblRecordingStatus = new Label
            {
                Text = "Recording: off",
                Left = 10,
                Top = 445,
                Width = 280,
                AutoEllipsis = true
            };

            _grpDevices.Controls.AddRange(new Control[]
            {
                _btnScan,
                _listDevices,
                _chkRecordSignals,
                _lblRecordingStatus
            });

            // Tacx Trainer Section
            _grpTacx = new GroupBox
            {
                Text = "Tacx Trainer",
                Left = 340,
                Top = 20,
                Width = 330,
                Height = 200
            };

            _btnDisconnectTacx = new Button
            {
                Text = "Disconnect Trainer",
                Left = 10,
                Top = 25,
                Width = 150,
                Enabled = false
            };

            _lblTacxStatus = new Label
            {
                Text = "Status: not connected",
                Left = 10,
                Top = 60,
                AutoSize = true
            };

            _lblTacxData = new Label
            {
                Text = "Power: -- W | Cadence: -- rpm | Speed: -- kph",
                Left = 10,
                Top = 85,
                AutoSize = true
            };

            _grpTacx.Controls.AddRange(new Control[]
            {
                _btnDisconnectTacx,
                _lblTacxStatus,
                _lblTacxData
            });

            // HRM Section
            _grpHrm = new GroupBox
            {
                Text = "Heart Rate Monitor",
                Left = 340,
                Top = 240,
                Width = 330,
                Height = 200
            };

            _btnDisconnectHrm = new Button
            {
                Text = "Disconnect HRM",
                Left = 10,
                Top = 25,
                Width = 150,
                Enabled = false
            };

            _lblHrmStatus = new Label
            {
                Text = "Status: not connected",
                Left = 10,
                Top = 60,
                AutoSize = true
            };

            _lblHeartRate = new Label
            {
                Text = "HR: -- bpm",
                Left = 10,
                Top = 85,
                AutoSize = true
            };

            _grpHrm.Controls.AddRange(new Control[]
            {
                _btnDisconnectHrm,
                _lblHrmStatus,
                _lblHeartRate
            });

            // Charts Section
            _grpCharts = new GroupBox
            {
                Text = "Training Data",
                Left = 690,
                Top = 20,
                Width = 680,
                Height = 1000
            };

            _chartPower = new TimeSeriesChart("Power", "W", Color.Red)
            {
                Left = 10,
                Top = 25,
                Width = 660,
                Height = 240
            };

            _chartCadence = new TimeSeriesChart("Cadence", "rpm", Color.Blue)
            {
                Left = 10,
                Top = 275,
                Width = 660,
                Height = 240
            };

            _chartSpeed = new TimeSeriesChart("Speed", "kph", Color.Green)
            {
                Left = 10,
                Top = 565,
                Width = 660,
                Height = 200
            };

            _chartHeartRate = new TimeSeriesChart("Heart Rate", "bpm", Color.Orange)
            {
                Left = 10,
                Top = 775,
                Width = 660,
                Height = 200
            };

            _grpCharts.Controls.AddRange(new Control[]
            {
                _chartPower,
                _chartCadence,
                _chartSpeed,
                _chartHeartRate
            });

            // Timer Section
            _grpTimer = new GroupBox
            {
                Text = "Training Timer",
                Left = 340,
                Top = 460,
                Width = 330,
                Height = 120
            };

            _trainingTimer = new TrainingTimerControl
            {
                Left = 10,
                Top = 25
            };
            _trainingTimer.Started += TrainingTimer_Started;
            _trainingTimer.Paused += TrainingTimer_Paused;
            _trainingTimer.Stopped += TrainingTimer_Stopped;

            _grpTimer.Controls.Add(_trainingTimer);

            Controls.AddRange(new Control[]
            {
                _grpDevices,
                _grpTacx,
                _grpHrm,
                _grpCharts,
                _grpTimer
            });

            // Event handlers
            _btnScan.Click += BtnScan_Click;
            _listDevices.DoubleClick += ListDevices_DoubleClick;
            _btnDisconnectTacx.Click += BtnDisconnectTacx_Click;
            _btnDisconnectHrm.Click += BtnDisconnectHrm_Click;
            _chkRecordSignals.CheckedChanged += ChkRecordSignals_CheckedChanged;
        }

        #region Device Scanning Methods

        private async void BtnScan_Click(object? sender, EventArgs e)
        {
            _btnScan.Enabled = false;
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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Scan failed: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
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
            
            _listDevices.Enabled = false;
            
            try
            {
                // Try to connect as Tacx trainer first
                bool isTacx = await BleTacxTrainer.IsTacxTrainerAsync(selectedDevice);
                if (isTacx)
                {
                    await ConnectToTacxAsync(selectedDevice);
                    return;
                }

                // If not Tacx, check if it's an HRM
                bool isHrm = await BleHeartRateSensor.IsHeartRateMonitorAsync(selectedDevice);
                if (isHrm)
                {
                    await ConnectToHrmAsync(selectedDevice);
                    return;
                }

                MessageBox.Show($"{selectedDevice.Name ?? "Device"} is not a Tacx trainer or Heart Rate Monitor.", "Device Not Supported", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainForm] Error checking device: {ex.Message}");
                MessageBox.Show($"Error checking device: {ex.Message}", "Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            finally
            {
                _listDevices.Enabled = true;
            }
        }

        #endregion

        private void ChkRecordSignals_CheckedChanged(object? sender, EventArgs e)
        {
            if (_changingRecordingState)
            {
                return;
            }

            try
            {
                if (_chkRecordSignals.Checked)
                {
                    var filePath = _signalRecorder.StartRecording();
                    _lblRecordingStatus.Text = $"Recording: {filePath}";
                }
                else
                {
                    _signalRecorder.StopRecording();
                    _lblRecordingStatus.Text = "Recording: off";
                }
            }
            catch (Exception ex)
            {
                _changingRecordingState = true;
                _chkRecordSignals.Checked = false;
                _changingRecordingState = false;
                _lblRecordingStatus.Text = "Recording: failed";
                MessageBox.Show($"Unable to start recording: {ex.Message}", "Recording Error", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void Device_RawDataReceived(object? sender, RawDeviceData data)
        {
            try
            {
                _signalRecorder.Record(data);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainForm] Unable to record raw device data: {ex.Message}");
            }
        }

        #region Tacx Trainer Methods

        private async Task ConnectToTacxAsync(BluetoothDevice device)
        {
            _lblTacxStatus.Text = "Status: connecting to trainer...";

            try
            {
                bool ok = await _tacxTrainer.ConnectToDeviceAsync(device);
                if (ok)
                {
                    _lblTacxStatus.Text = $"Status: connected to {device.Name ?? "trainer"}";
                    _btnDisconnectTacx.Enabled = true;
                    _lblTacxData.Text = "Power: -- W | Cadence: -- rpm | Speed: -- kph";
                    
                    // Don't start chart recording automatically - wait for timer start
                    
                    // Remove device from list when connected
                    int index = _availableDevices.IndexOf(device);
                    if (index >= 0)
                    {
                        _listDevices.Items.RemoveAt(index);
                        _availableDevices.RemoveAt(index);
                    }
                }
                else
                {
                    _lblTacxStatus.Text = "Status: connection failed";
                }
            }
            catch (Exception ex)
            {
                _lblTacxStatus.Text = $"Status: connection error - {ex.Message}";
            }
        }

        private async void BtnDisconnectTacx_Click(object? sender, EventArgs e)
        {
            _lblTacxStatus.Text = "Status: disconnecting...";
            await _tacxTrainer.DisconnectAsync();
            _lblTacxStatus.Text = "Status: trainer disconnected";
            _btnDisconnectTacx.Enabled = false;
            _lblTacxData.Text = "Power: -- W | Cadence: -- rpm | Speed: -- kph";
            
            // Stop chart recording
            _chartPower.StopRecording();
            _chartCadence.StopRecording();
            _chartSpeed.StopRecording();
        }

        private void TacxTrainer_DataUpdated(object? sender, TrainerData data)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => TacxTrainer_DataUpdated(sender, data)));
                return;
            }

            _lblTacxData.Text = $"Power: {data.PowerWatts:F0} W | Cadence: {data.CadenceRpm:F0} rpm | Speed: {data.SpeedKph:F1} kph";
            
            // Only update charts if timer is running and not paused
            if (_trainingTimer.IsRunning)
            {
                var timestamp = DateTime.Now;
                _chartPower.AddDataPoint(data.PowerWatts, timestamp);
                _chartCadence.AddDataPoint(data.CadenceRpm, timestamp);
                _chartSpeed.AddDataPoint(data.SpeedKph, timestamp);
            }
        }

        #endregion

        #region HRM Methods

        private async Task ConnectToHrmAsync(BluetoothDevice device)
        {
            _lblHrmStatus.Text = "Status: connecting to HRM...";

            try
            {
                bool ok = await _hrmSensor.ConnectToDeviceAsync(device);
                if (ok)
                {
                    _lblHrmStatus.Text = $"Status: connected to {device.Name ?? "HRM"}";
                    _btnDisconnectHrm.Enabled = true;
                    _lblHeartRate.Text = "HR: -- bpm";
                    
                    // Start heart rate chart recording
                    _chartHeartRate.StartRecording();
                    
                    // Remove device from list when connected
                    int index = _availableDevices.IndexOf(device);
                    if (index >= 0)
                    {
                        _listDevices.Items.RemoveAt(index);
                        _availableDevices.RemoveAt(index);
                    }
                }
                else
                {
                    _lblHrmStatus.Text = "Status: connection failed";
                }
            }
            catch (Exception ex)
            {
                _lblHrmStatus.Text = $"Status: connection error - {ex.Message}";
            }
        }

        private async void BtnDisconnectHrm_Click(object? sender, EventArgs e)
        {
            _lblHrmStatus.Text = "Status: disconnecting...";
            await _hrmSensor.DisconnectAsync();
            _lblHrmStatus.Text = "Status: HRM disconnected";
            _btnDisconnectHrm.Enabled = false;
            _lblHeartRate.Text = "HR: -- bpm";
            
            // Stop heart rate chart recording
            _chartHeartRate.StopRecording();
        }

        private void HrmSensor_HeartRateReceived(object? sender, int bpm)
        {
            if (InvokeRequired)
            {
                BeginInvoke(new Action(() => HrmSensor_HeartRateReceived(sender, bpm)));
                return;
            }

            _lblHeartRate.Text = $"HR: {bpm} bpm";
            
            // Update heart rate chart (only if timer is running and not paused)
            if (_trainingTimer.IsRunning)
            {
                var timestamp = DateTime.Now;
                _chartHeartRate.AddDataPoint(bpm, timestamp);
            }
        }

        #endregion

        #region Timer Methods

        private void TrainingTimer_Started(object? sender, EventArgs e)
        {
            // Start chart recording when timer starts (only if not resuming from pause)
            if (!_chartPower.IsRecording)
            {
                _chartPower.StartRecording();
                _chartCadence.StartRecording();
                _chartSpeed.StartRecording();
            }
        }

        private void TrainingTimer_Paused(object? sender, EventArgs e)
        {
            // Charts will stop updating automatically since IsRunning returns false
        }

        private void TrainingTimer_Stopped(object? sender, EventArgs e)
        {
            // Stop chart recording
            _chartPower.StopRecording();
            _chartCadence.StopRecording();
            _chartSpeed.StopRecording();
        }

        #endregion

        protected override async void OnFormClosed(FormClosedEventArgs e)
        {
            _signalRecorder.Dispose();
            _tacxTrainer.RawDataReceived -= Device_RawDataReceived;
            _hrmSensor.RawDataReceived -= Device_RawDataReceived;
            base.OnFormClosed(e);
            
            await _tacxTrainer.DisconnectAsync();
            await _hrmSensor.DisconnectAsync();
        }
    }
}
