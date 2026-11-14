using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using ErgTrainer.Controls;
using ErgTrainer.Recorders;
using ErgTrainer.Sensors;
using ErgTrainer.Sessions;
using InTheHand.Bluetooth;

namespace ErgTrainer
{
    public class MainForm : Form
    {
        private readonly BleTacxTrainer _tacxTrainer;
        private readonly BleHeartRateSensor _hrmSensor;
        private readonly SignalRecorder _signalRecorder;
        private readonly SessionEventLog _sessionEventLog = new();
        private ActivityRecorder? _activityRecorder;
        private Task? _activityFinalization;
        private readonly TextBox _activityStatus;
        private readonly Button _btnOpenActivityFolder;
        private string? _lastActivityPath;
        private TrainerData? _latestActivityTrainer;
        private DateTimeOffset _latestActivityTrainerAt;
        private int? _latestActivityHeartRate;
        private DateTimeOffset _latestActivityHeartRateAt;
        private long _lastActivitySampleSecond = -1;
        private int _lastActivityBlockIndex;
        private bool _activityErrorShown;

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
        private readonly Button _btnRetryTacx;
        private readonly Button _btnTestFtmsControl;
        private readonly Button _btnSetTargetPower;
        private readonly Button _btnTestSlope;
        private readonly NumericUpDown _targetPower;
        private readonly Label _targetPowerLabel;
        private readonly NumericUpDown _testGradient;
        private readonly Label _testGradientLabel;
        private readonly NumericUpDown _testRiderWeight;
        private readonly Label _testRiderWeightLabel;
        private readonly NumericUpDown _testBikeWeight;
        private readonly Label _testBikeWeightLabel;
        private readonly Label _lblTacxStatus;
        private readonly Label _lblTacxData;
        private readonly Label _lblFtmsControlResponse;
        private readonly NumericUpDown _wheelCircumference;

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
        private readonly SessionBuilderControl _sessionBuilder;
        private readonly SessionClock _sessionClock = new();
        private readonly System.Windows.Forms.Timer _sessionTicker = new() { Interval = 100 };
        private TrainingSession? _runningSession;
        private int _lastCommandedBlock = -1;
        private int _sessionGeneration;
        private bool _sessionCommandPending;
        private bool _manualControlPending;
        private CancellationTokenSource? _manualControlCancellation;
        private CancellationTokenSource? _sessionCancellation;
        private BluetoothDevice? _connectedTrainerDevice;
        private BluetoothDevice? _trainerDeviceForRecovery;
        private CancellationTokenSource? _reconnectCancellation;
        private bool _sessionRecovering;
        private bool _reconnectInProgress;
        private bool _closingForCleanup;
        private bool _cleanupFinished;

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
                Height = 360
            };

            _btnDisconnectTacx = new Button
            {
                Text = "Disconnect Trainer",
                Left = 10,
                Top = 25,
                Width = 150,
                Enabled = false
            };

            _btnRetryTacx = new Button
            {
                Text = "Retry trainer", Left = 165, Top = 25, Width = 150,
                Enabled = false, Visible = false
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
                Top = 100,
                AutoSize = true
            };

            _btnTestFtmsControl = new Button
            {
                Text = "Test FTMS control",
                Left = 10,
                Top = 130,
                Width = 150,
                Enabled = false,
                Visible = false
            };

            _targetPowerLabel = new Label
            {
                Text = "ERG watts:",
                Left = 10,
                Top = 134,
                AutoSize = true,
                Visible = false
            };
            _targetPower = new NumericUpDown
            {
                Left = 85,
                Top = 130,
                Width = 70,
                Minimum = 0,
                Maximum = 1000,
                Value = 150,
                Visible = false
            };
            _btnSetTargetPower = new Button
            {
                Text = "Set ERG target",
                Left = 165,
                Top = 130,
                Width = 150,
                Enabled = false,
                Visible = false
            };

            _testGradientLabel = new Label { Text = "Slope %:", Left = 10, Top = 169, AutoSize = true, Visible = false };
            _testGradient = new NumericUpDown
            {
                Left = 85, Top = 165, Width = 70, Minimum = -20, Maximum = 20,
                DecimalPlaces = 1, Increment = .5m, Visible = false
            };
            _btnTestSlope = new Button
            {
                Text = "Set test slope", Left = 165, Top = 165, Width = 150,
                Enabled = false, Visible = false
            };
            _testRiderWeightLabel = new Label { Text = "Rider kg:", Left = 10, Top = 204, AutoSize = true, Visible = false };
            _testRiderWeight = new NumericUpDown
            {
                Left = 85, Top = 200, Width = 70, Minimum = .01m, Maximum = 655.34m,
                DecimalPlaces = 2, Increment = .5m, Value = 75, Visible = false
            };
            _testBikeWeightLabel = new Label { Text = "Bike kg:", Left = 165, Top = 204, AutoSize = true, Visible = false };
            _testBikeWeight = new NumericUpDown
            {
                Left = 235, Top = 200, Width = 70, Minimum = .01m, Maximum = 50,
                DecimalPlaces = 2, Increment = .5m, Value = 10, Visible = false
            };

            var wheelCircumferenceLabel = new Label
            {
                Text = "Wheel mm:",
                Left = 10,
                Top = 239,
                AutoSize = true
            };

            _wheelCircumference = new NumericUpDown
            {
                Left = 85,
                Top = 235,
                Width = 75,
                Minimum = 500,
                Maximum = 4000,
                Value = _tacxTrainer.WheelCircumferenceMm
            };
            _wheelCircumference.ValueChanged += (_, _) =>
                _tacxTrainer.WheelCircumferenceMm = (int)_wheelCircumference.Value;

            _lblFtmsControlResponse = new Label
            {
                Text = "Control: not connected",
                Left = 10,
                Top = 275,
                Width = 300,
                Height = 70
            };

            _grpTacx.Controls.AddRange(new Control[]
            {
                _btnDisconnectTacx,
                _btnRetryTacx,
                _lblTacxStatus,
                _lblTacxData,
                _btnTestFtmsControl,
                _targetPowerLabel,
                _targetPower,
                _btnSetTargetPower,
                _testGradientLabel,
                _testGradient,
                _btnTestSlope,
                _testRiderWeightLabel,
                _testRiderWeight,
                _testBikeWeightLabel,
                _testBikeWeight,
                wheelCircumferenceLabel,
                _wheelCircumference,
                _lblFtmsControlResponse
            });

            // HRM Section
            _grpHrm = new GroupBox
            {
                Text = "Heart Rate Monitor",
                Left = 340,
                Top = 400,
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
                Top = 620,
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

            _activityStatus = new TextBox
            {
                Left = 340, Top = 750, Width = 330, Height = 80,
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical,
                Text = "Activity: no session recorded yet."
            };
            _btnOpenActivityFolder = new Button
            {
                Left = 340, Top = 840, Width = 150, Text = "Open activity folder",
                Enabled = false
            };
            _btnOpenActivityFolder.Click += (_, _) => OpenActivityFolder();

            var tabs = new TabControl { Dock = DockStyle.Fill };
            var trainerTab = new TabPage("Trainer") { AutoScroll = true };
            trainerTab.Controls.AddRange(new Control[] { _grpDevices, _grpTacx, _grpHrm, _grpCharts, _grpTimer,
                _activityStatus, _btnOpenActivityFolder });
            var builderTab = new TabPage("Session Builder");
            _sessionBuilder = new SessionBuilderControl();
            builderTab.Controls.Add(_sessionBuilder);
            tabs.TabPages.Add(trainerTab);
            tabs.TabPages.Add(builderTab);
            Controls.Add(tabs);
            _sessionBuilder.StartRequested += StartSessionAsync;
            _sessionBuilder.PauseRequested += PauseSession;
            _sessionBuilder.ResumeRequested += ResumeSession;
            _sessionBuilder.StopRequested += StopSession;
            _sessionTicker.Tick += SessionTicker_Tick;

            // Event handlers
            _btnScan.Click += BtnScan_Click;
            _listDevices.DoubleClick += ListDevices_DoubleClick;
            _btnDisconnectTacx.Click += BtnDisconnectTacx_Click;
            _btnRetryTacx.Click += BtnRetryTacx_Click;
            _btnTestFtmsControl.Click += BtnTestFtmsControl_Click;
            _btnSetTargetPower.Click += BtnSetTargetPower_Click;
            _btnTestSlope.Click += BtnTestSlope_Click;
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
            if (_sessionRecovering)
            {
                _lblTacxStatus.Text = "Status: use Retry trainer or stop the paused session first";
                return;
            }
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

        private async void ChkRecordSignals_CheckedChanged(object? sender, EventArgs e)
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
                    await SampleTrainerCharacteristicsAsync();
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

        private async Task SampleTrainerCharacteristicsAsync()
        {
            if (_tacxTrainer.CharacteristicCount is null || !_signalRecorder.IsRecording)
            {
                return;
            }

            try
            {
                int sampled = await _tacxTrainer.SampleOtherCharacteristicsAsync();
                Debug.WriteLine($"[MainForm] Sampled {sampled} additional trainer characteristic(s).");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainForm] Unable to sample trainer characteristics: {ex.Message}");
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
                    _trainerDeviceForRecovery = device;
                    _btnRetryTacx.Enabled = false;
                    _btnRetryTacx.Visible = false;
                    string cscStatus = _tacxTrainer.HasCyclingSpeedCadenceNotifications ? "on" : "unavailable";
                    _lblTacxStatus.Text = $"Status: {device.Name ?? "trainer"} | CSC: {cscStatus}\nControl: {_tacxTrainer.ControlMethod}";
                    _btnDisconnectTacx.Enabled = true;
                    if (_connectedTrainerDevice != null)
                        _connectedTrainerDevice.GattServerDisconnected -= TrainerDevice_GattServerDisconnected;
                    _connectedTrainerDevice = device;
                    device.GattServerDisconnected += TrainerDevice_GattServerDisconnected;
                    bool fec = _tacxTrainer.ControlMethod == "Tacx FE-C over BLE";
                    bool ftms = _tacxTrainer.ControlMethod == "FTMS";
                    _btnSetTargetPower.Visible = fec;
                    _btnSetTargetPower.Enabled = fec;
                    _targetPower.Visible = fec;
                    _targetPowerLabel.Visible = fec;
                    SetSlopeControlsVisible(fec);
                    _btnTestSlope.Enabled = fec;
                    _btnTestFtmsControl.Visible = ftms;
                    _btnTestFtmsControl.Enabled = ftms;
                    _lblFtmsControlResponse.Text = fec ? "Control: ready for ERG or slope test" :
                        ftms ? "Control: FTMS test available" : "Control: unavailable";
                    _lblTacxData.Text = "Power: -- W | Cadence: -- rpm | Speed: -- kph";
                    await SampleTrainerCharacteristicsAsync();
                    
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
                    await _tacxTrainer.DisconnectAsync();
                }
            }
            catch (Exception ex)
            {
                _lblTacxStatus.Text = $"Status: connection error - {ex.Message}";
                try { await _tacxTrainer.DisconnectAsync(); }
                catch (Exception cleanupEx) { Debug.WriteLine($"[MainForm] Trainer cleanup failed: {cleanupEx}"); }
            }
        }

        private async void BtnDisconnectTacx_Click(object? sender, EventArgs e)
        {
            if (_sessionClock.IsActive || _sessionCommandPending) StopSession();
            _reconnectCancellation?.Cancel();
            _trainerDeviceForRecovery = null;
            _sessionRecovering = false;
            _btnRetryTacx.Enabled = false;
            _btnRetryTacx.Visible = false;
            _manualControlCancellation?.Cancel();
            _btnDisconnectTacx.Enabled = false;
            _btnSetTargetPower.Enabled = false;
            _btnTestSlope.Enabled = false;
            if (_connectedTrainerDevice != null)
                _connectedTrainerDevice.GattServerDisconnected -= TrainerDevice_GattServerDisconnected;
            _connectedTrainerDevice = null;
            _lblTacxStatus.Text = "Status: disconnecting...";
            await _tacxTrainer.DisconnectAsync();
            _lblTacxStatus.Text = "Status: trainer disconnected";
            _btnDisconnectTacx.Enabled = false;
            _btnTestFtmsControl.Enabled = false;
            _btnTestFtmsControl.Visible = false;
            _btnSetTargetPower.Enabled = false;
            _btnSetTargetPower.Visible = false;
            _targetPower.Visible = false;
            _targetPowerLabel.Visible = false;
            SetSlopeControlsVisible(false);
            _lblFtmsControlResponse.Text = "Control: not connected";
            _lblTacxData.Text = "Power: -- W | Cadence: -- rpm | Speed: -- kph";
            
            // Stop chart recording
            _chartPower.StopRecording();
            _chartCadence.StopRecording();
            _chartSpeed.StopRecording();
        }

        private async void BtnTestFtmsControl_Click(object? sender, EventArgs e)
        {
            _btnTestFtmsControl.Enabled = false;
            _lblFtmsControlResponse.Text = "Control test: waiting for reply...";

            try
            {
                string result = await _tacxTrainer.TestFtmsControlAsync();
                if (!IsDisposed)
                {
                    _lblFtmsControlResponse.Text = result;
                    MessageBox.Show(result, "FTMS Control Test", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
            }
            catch (TimeoutException)
            {
                if (!IsDisposed)
                {
                    _lblFtmsControlResponse.Text = "Control test: no response within 5 seconds";
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainForm] FTMS control test failed: {ex}");
                if (!IsDisposed)
                {
                    _lblFtmsControlResponse.Text = $"Control test failed: {ex.Message}";
                    MessageBox.Show(ex.Message, "FTMS Control Test", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
            finally
            {
                if (!IsDisposed)
                {
                    _btnTestFtmsControl.Enabled = _btnDisconnectTacx.Enabled;
                }
            }
        }

        private async void BtnSetTargetPower_Click(object? sender, EventArgs e)
        {
            if (_sessionClock.IsActive || _sessionCommandPending || _manualControlPending) return;
            _manualControlPending = true;
            _btnSetTargetPower.Enabled = false;
            _btnTestSlope.Enabled = false;
            int watts = (int)_targetPower.Value;
            _lblFtmsControlResponse.Text = $"Control: sending {watts} W target...";
            try
            {
                _lblFtmsControlResponse.Text = await _tacxTrainer.SetTargetPowerAsync(watts);
            }
            catch (TimeoutException ex)
            {
                _lblFtmsControlResponse.Text = $"Control: {ex.Message}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainForm] FE-C ERG command failed: {ex}");
                _lblFtmsControlResponse.Text = $"Control failed: {ex.Message}";
            }
            finally
            {
                _manualControlPending = false;
                if (!IsDisposed)
                {
                    _btnSetTargetPower.Enabled = _btnDisconnectTacx.Enabled &&
                        _tacxTrainer.ControlMethod == "Tacx FE-C over BLE";
                    _btnTestSlope.Enabled = _btnSetTargetPower.Enabled;
                }
            }
        }

        private async void BtnTestSlope_Click(object? sender, EventArgs e)
        {
            if (_sessionClock.IsActive || _sessionCommandPending || _manualControlPending) return;
            _manualControlPending = true;
            _btnTestSlope.Enabled = false;
            _btnSetTargetPower.Enabled = false;
            _manualControlCancellation = new CancellationTokenSource();
            decimal gradient = _testGradient.Value;
            _lblFtmsControlResponse.Text = $"Control: testing {gradient:0.#}% slope...";
            try
            {
                var result = await _tacxTrainer.TestSimulationSlopeAsync(
                    gradient, _testRiderWeight.Value, _testBikeWeight.Value,
                    status => { if (!IsDisposed) _lblFtmsControlResponse.Text = status; },
                    _manualControlCancellation.Token);
                if (!IsDisposed) _lblFtmsControlResponse.Text = result.Message;
            }
            catch (OperationCanceledException)
            {
                if (!IsDisposed) _lblFtmsControlResponse.Text = "Control: slope test cancelled.";
            }
            catch (TimeoutException ex)
            {
                if (!IsDisposed) _lblFtmsControlResponse.Text = $"Control: {ex.Message}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainForm] FE-C slope test failed: {ex}");
                if (!IsDisposed) _lblFtmsControlResponse.Text = $"Slope test failed: {ex.Message}";
            }
            finally
            {
                _manualControlCancellation.Dispose();
                _manualControlCancellation = null;
                _manualControlPending = false;
                if (!IsDisposed)
                {
                    _btnTestSlope.Enabled = _btnDisconnectTacx.Enabled &&
                        _tacxTrainer.ControlMethod == "Tacx FE-C over BLE";
                    _btnSetTargetPower.Enabled = _btnTestSlope.Enabled;
                }
            }
        }

        private void SetSlopeControlsVisible(bool visible)
        {
            _btnTestSlope.Visible = visible;
            _testGradient.Visible = visible;
            _testGradientLabel.Visible = visible;
            _testRiderWeight.Visible = visible;
            _testRiderWeightLabel.Visible = visible;
            _testBikeWeight.Visible = visible;
            _testBikeWeightLabel.Visible = visible;
        }

        private void TacxTrainer_DataUpdated(object? sender, TrainerData data)
        {
            if (IsDisposed || _closingForCleanup || !IsHandleCreated) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new Action(() => TacxTrainer_DataUpdated(sender, data))); }
                catch (InvalidOperationException) { }
                return;
            }

            string cadence = data.CadenceRpm?.ToString("F0") ?? "--";
            string speed = data.SpeedKph?.ToString("F1") ?? "--";
            _lblTacxData.Text = $"Power: {data.PowerWatts:F0} W | Cadence: {cadence} rpm | Speed: {speed} kph";
            if (_sessionClock.IsRunning && _activityRecorder != null)
            {
                _latestActivityTrainer = data;
                _latestActivityTrainerAt = DateTimeOffset.UtcNow;
            }
            
            // Only update charts if timer is running and not paused
            if (_trainingTimer.IsRunning || _sessionClock.IsRunning)
            {
                var timestamp = DateTime.Now;
                _chartPower.AddDataPoint(data.PowerWatts, timestamp);
                if (data.CadenceRpm is double cadenceRpm)
                {
                    _chartCadence.AddDataPoint(cadenceRpm, timestamp);
                }
                if (data.SpeedKph is double speedKph)
                {
                    _chartSpeed.AddDataPoint(speedKph, timestamp);
                }
            }
        }

        #endregion

        private async void TrainerDevice_GattServerDisconnected(object? sender, EventArgs e)
        {
            if (InvokeRequired)
            {
                if (IsHandleCreated && !IsDisposed)
                {
                    try { BeginInvoke(new Action(() => TrainerDevice_GattServerDisconnected(sender, e))); }
                    catch (InvalidOperationException) { }
                }
                return;
            }
            if (_connectedTrainerDevice == null || IsDisposed || _closingForCleanup) return;
            var device = _connectedTrainerDevice;
            Debug.WriteLine("[MainForm] Trainer GATT connection lost.");
            _manualControlCancellation?.Cancel();
            device.GattServerDisconnected -= TrainerDevice_GattServerDisconnected;
            _connectedTrainerDevice = null;
            bool activeSession = _sessionClock.IsActive && _runningSession != null;
            if (activeSession)
            {
                _sessionClock.Pause();
                RecordActivityEvent("disconnect");
                _latestActivityTrainer = null;
                ++_sessionGeneration;
                _sessionCancellation?.Cancel();
                _sessionCancellation?.Dispose();
                _sessionCancellation = new CancellationTokenSource();
                _sessionCommandPending = false;
                _sessionRecovering = true;
                _trainerDeviceForRecovery = device;
                _sessionEventLog.Write("trainer_disconnected", "Session paused; starting reconnect.");
                _sessionBuilder.SetRecoveryState("Trainer connection lost. Session paused; reconnecting...");
                _sessionBuilder.ShowPosition(_runningSession!, _sessionClock.Position, false);
            }
            else if (_sessionCommandPending)
            {
                _sessionEventLog.Write("trainer_disconnected", "Connection lost during session start.");
                StopSession();
            }
            _btnDisconnectTacx.Enabled = false;
            _btnSetTargetPower.Enabled = false;
            _btnSetTargetPower.Visible = false;
            _targetPower.Visible = false;
            _targetPowerLabel.Visible = false;
            _btnTestSlope.Enabled = false;
            SetSlopeControlsVisible(false);
            _btnTestFtmsControl.Enabled = false;
            _btnTestFtmsControl.Visible = false;
            _lblTacxStatus.Text = activeSession ? "Status: trainer lost; reconnecting..." : "Status: trainer connection lost";
            _lblFtmsControlResponse.Text = "Control: disconnected";
            try { await _tacxTrainer.DisconnectAsync(); }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MainForm] Trainer disconnect cleanup failed: {ex}");
                _sessionEventLog.Write("disconnect_cleanup_failed", ex.Message);
            }
            if (activeSession && _sessionRecovering && !_closingForCleanup)
                await RecoverTrainerAsync(device);
        }

        private async void BtnRetryTacx_Click(object? sender, EventArgs e)
        {
            if (_sessionRecovering && _trainerDeviceForRecovery is { } device)
                await RecoverTrainerAsync(device);
        }

        private async Task RecoverTrainerAsync(BluetoothDevice device)
        {
            if (_reconnectInProgress || !_sessionRecovering || _runningSession == null) return;
            _reconnectInProgress = true;
            _btnRetryTacx.Enabled = false;
            _btnRetryTacx.Visible = false;
            using var cancellation = new CancellationTokenSource();
            _reconnectCancellation = cancellation;
            try
            {
                for (int attempt = 1; attempt <= 3; attempt++)
                {
                    cancellation.Token.ThrowIfCancellationRequested();
                    if (attempt > 1)
                        await Task.Delay(TimeSpan.FromSeconds(attempt == 2 ? 2 : 5), cancellation.Token);
                    _sessionBuilder.SetRecoveryState($"Trainer disconnected. Reconnect attempt {attempt}/3...");
                    _sessionEventLog.Write("reconnect_attempt", attempt.ToString());
                    try
                    {
                        bool connected = await _tacxTrainer.ConnectToDeviceAsync(device, cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (!connected || _tacxTrainer.ControlMethod != "Tacx FE-C over BLE")
                            throw new InvalidOperationException("FE-C trainer control is unavailable after reconnect.");

                        var session = _runningSession ?? throw new OperationCanceledException();
                        int blockIndex = _sessionClock.Position.BlockIndex;
                        int watts = session.Blocks[blockIndex].TargetWatts!.Value;
                        var result = await _tacxTrainer.SetTargetPowerWithResultAsync(watts, cancellation.Token);
                        cancellation.Token.ThrowIfCancellationRequested();
                        if (!result.Accepted)
                            throw new InvalidOperationException(result.Message);
                        if (!device.Gatt.IsConnected)
                            throw new InvalidOperationException("Trainer disconnected again while restoring the target.");

                        _connectedTrainerDevice = device;
                        device.GattServerDisconnected += TrainerDevice_GattServerDisconnected;
                        _sessionRecovering = false;
                        _reconnectCancellation = null;
                        _lastCommandedBlock = blockIndex;
                        _btnDisconnectTacx.Enabled = true;
                        _btnSetTargetPower.Visible = true;
                        _targetPower.Visible = true;
                        _targetPowerLabel.Visible = true;
                        SetSlopeControlsVisible(true);
                        _lblTacxStatus.Text = "Status: trainer reconnected; session paused";
                        _lblFtmsControlResponse.Text = result.Message;
                        _sessionBuilder.SetRunState(true, false, "Trainer restored the current ERG target. Press Resume when ready.");
                        _sessionEventLog.Write("trainer_reconnected", $"Block {blockIndex + 1}; {watts} W restored.");
                        return;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[MainForm] Reconnect attempt {attempt} failed: {ex}");
                        _sessionEventLog.Write("reconnect_failed", $"Attempt {attempt}: {ex.GetType().Name}: {ex.Message}");
                        try { await _tacxTrainer.DisconnectAsync(); }
                        catch (Exception cleanupEx) { Debug.WriteLine($"[MainForm] Reconnect cleanup failed: {cleanupEx}"); }
                    }
                }
                if (_sessionRecovering && !IsDisposed)
                {
                    _btnRetryTacx.Visible = true;
                    _btnRetryTacx.Enabled = true;
                    _lblTacxStatus.Text = "Status: trainer disconnected; retry available";
                    _sessionBuilder.SetRecoveryState("Trainer is still disconnected. Use Retry trainer or Stop session.");
                }
            }
            catch (OperationCanceledException)
            {
                try { await _tacxTrainer.DisconnectAsync(); }
                catch (Exception ex) { Debug.WriteLine($"[MainForm] Cancelled reconnect cleanup failed: {ex}"); }
            }
            finally
            {
                if (ReferenceEquals(_reconnectCancellation, cancellation)) _reconnectCancellation = null;
                _reconnectInProgress = false;
            }
        }

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
            _latestActivityHeartRate = null;
            
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
            if (_sessionClock.IsRunning && _activityRecorder != null)
            {
                _latestActivityHeartRate = bpm;
                _latestActivityHeartRateAt = DateTimeOffset.UtcNow;
            }
            
            // Update heart rate chart (only if timer is running and not paused)
            if (_trainingTimer.IsRunning || _sessionClock.IsRunning)
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

        private async void StartSessionAsync(TrainingSession session)
        {
            if (_sessionClock.IsActive || _sessionCommandPending || _manualControlPending) return;
            if (_activityFinalization is { IsCompleted: false })
            {
                _sessionBuilder.SetRunState(false, false, "Wait for the previous FIT activity to finish exporting.");
                return;
            }
            if (_trainingTimer.IsActive)
            {
                _sessionBuilder.SetRunState(false, false, "Stop the free-ride timer before starting a session.");
                return;
            }
            if (!_btnDisconnectTacx.Enabled || _tacxTrainer.ControlMethod != "Tacx FE-C over BLE")
            {
                _sessionBuilder.SetRunState(false, false, "Connect a trainer with Tacx FE-C-over-BLE ERG control first.");
                return;
            }
            if (session.Mode != "erg")
            {
                _sessionBuilder.SetRunState(false, false, "Simulation hardware execution is unavailable: gradient blocks are not yet wired to trainer control.");
                return;
            }

            int generation = ++_sessionGeneration;
            _sessionEventLog.Start();
            _sessionEventLog.Write("session_start_requested", $"First ERG target {session.Blocks[0].TargetWatts} W.");
            _sessionCancellation = new CancellationTokenSource();
            _sessionCommandPending = true;
            _btnSetTargetPower.Enabled = false;
            _btnTestSlope.Enabled = false;
            _sessionBuilder.SetRunState(true, false, "Sending first ERG target...");
            _sessionBuilder.SetStartPending();
            try
            {
                var result = await _tacxTrainer.SetTargetPowerWithResultAsync(
                    session.Blocks[0].TargetWatts!.Value, _sessionCancellation.Token);
                if (generation != _sessionGeneration || IsDisposed) return;
                if (!result.Accepted)
                {
                    _sessionEventLog.Write("session_start_rejected", result.Message);
                    _sessionEventLog.Stop();
                    _sessionBuilder.SetRunState(false, false, result.Message);
                    _btnSetTargetPower.Enabled = true;
                    _btnTestSlope.Enabled = true;
                    return;
                }
                _runningSession = session;
                _sessionEventLog.Write("session_started", $"Block 1; {session.Blocks[0].TargetWatts} W.");
                _lastCommandedBlock = 0;
                var startedUtc = DateTimeOffset.UtcNow;
                _sessionClock.Start(session);
                StartActivityRecording(session, startedUtc);
                _trainingTimer.Enabled = false;
                _chartPower.StartRecording();
                _chartCadence.StartRecording();
                _chartSpeed.StartRecording();
                _sessionTicker.Start();
                _sessionBuilder.SetRunState(true, true, result.Message +
                    (_sessionEventLog.CurrentFilePath == null ? " Session event log is unavailable." : ""));
                _sessionBuilder.ShowPosition(session, _sessionClock.Position, true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Session] First ERG command failed: {ex}");
                _sessionEventLog.Write("session_start_failed", $"{ex.GetType().Name}: {ex.Message}");
                _sessionEventLog.Stop();
                if (generation == _sessionGeneration && !IsDisposed)
                {
                    _sessionBuilder.SetRunState(false, false, $"Session start failed: {ex.Message}");
                    _btnSetTargetPower.Enabled = true;
                    _btnTestSlope.Enabled = true;
                }
            }
            finally
            {
                if (generation == _sessionGeneration)
                {
                    _sessionCommandPending = false;
                    if (_runningSession == null)
                    {
                        _sessionCancellation?.Dispose();
                        _sessionCancellation = null;
                    }
                }
            }
        }

        private void SessionTicker_Tick(object? sender, EventArgs e)
        {
            var session = _runningSession;
            if (session == null || _sessionRecovering) return;
            var position = _sessionClock.Position;
            if (position.Finished)
            {
                _sessionEventLog.Write("session_complete");
                StopSession("complete");
                _sessionBuilder.SetRunState(false, false, "Session complete. Trainer retains its last target until changed or disconnected.");
                return;
            }
            if (_sessionClock.IsRunning) RecordActivitySample();
            _sessionBuilder.ShowPosition(session, position, _sessionClock.IsRunning);
            if (position.BlockIndex != _lastActivityBlockIndex)
            {
                RecordActivityEvent("block", session.Blocks[position.BlockIndex].Name, position.BlockIndex);
                _lastActivityBlockIndex = position.BlockIndex;
            }
            if (!_sessionCommandPending && position.BlockIndex != _lastCommandedBlock)
                _ = CommandBlockAsync(session, position.BlockIndex, _sessionGeneration);
        }

        private async Task CommandBlockAsync(TrainingSession session, int index, int generation)
        {
            _sessionCommandPending = true;
            try
            {
                var block = session.Blocks[index];
                var result = await _tacxTrainer.SetTargetPowerWithResultAsync(
                    block.TargetWatts!.Value, _sessionCancellation?.Token ?? default);
                if (generation != _sessionGeneration || IsDisposed) return;
                if (!result.Accepted)
                {
                    _sessionEventLog.Write("block_command_rejected", $"Block {index + 1}: {result.Message}");
                    StopSession();
                    _sessionBuilder.SetRunState(false, false, $"Stopped at {block.Name}: {result.Message}");
                    return;
                }
                _lastCommandedBlock = index;
                _sessionEventLog.Write("block_target_accepted", $"Block {index + 1}; {block.TargetWatts} W.");
                _sessionBuilder.SetRunState(true, _sessionClock.IsRunning, result.Message);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Session] Block command failed: {ex}");
                if (generation == _sessionGeneration && !IsDisposed)
                {
                    _sessionEventLog.Write("block_command_failed", $"Block {index + 1}: {ex.GetType().Name}: {ex.Message}");
                    StopSession();
                    _sessionBuilder.SetRunState(false, false, $"Stopped: trainer command failed: {ex.Message}");
                }
            }
            finally
            {
                if (generation == _sessionGeneration) _sessionCommandPending = false;
            }
        }

        private void PauseSession()
        {
            if (!_sessionClock.IsRunning || _sessionRecovering) return;
            _sessionClock.Pause();
            RecordActivityEvent("pause");
            _sessionEventLog.Write("session_paused");
            _sessionBuilder.SetRunState(true, false, "Session paused. Trainer retains its current target.");
            if (_runningSession != null) _sessionBuilder.ShowPosition(_runningSession, _sessionClock.Position, false);
        }

        private void ResumeSession()
        {
            if (!_sessionClock.IsActive || _sessionClock.IsRunning || _sessionRecovering ||
                _connectedTrainerDevice == null) return;
            _sessionClock.Resume();
            _latestActivityTrainer = null;
            _latestActivityHeartRate = null;
            RecordActivityEvent("resume");
            _sessionEventLog.Write("session_resumed");
            _sessionBuilder.SetRunState(true, true, "Session resumed.");
        }

        private void StartActivityRecording(TrainingSession session, DateTimeOffset startedUtc)
        {
            _latestActivityTrainer = null;
            _latestActivityHeartRate = null;
            _lastActivitySampleSecond = -1;
            _lastActivityBlockIndex = 0;
            _activityErrorShown = false;
            try
            {
                _activityRecorder = new ActivityRecorder(session.Name, startedUtc);
                _lastActivityPath = _activityRecorder.JournalPath;
                _activityStatus.Text = $"Recording activity: {_lastActivityPath}";
                _btnOpenActivityFolder.Enabled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ActivityRecorder] Unable to start: {ex}");
                _lastActivityPath = null;
                _btnOpenActivityFolder.Enabled = false;
                _activityStatus.Text = $"Activity recording failed: {ex.Message}";
            }
        }

        private void RecordActivityEvent(string kind, string? name = null, int? blockIndex = null)
        {
            if (_activityRecorder == null) return;
            bool ok = _activityRecorder.Add(new ActivityEntry(kind, DateTimeOffset.UtcNow,
                name, blockIndex, ActiveSeconds: _sessionClock.Elapsed.TotalSeconds));
            if (!ok) ShowActivityError();
        }

        private void RecordActivitySample()
        {
            if (_activityRecorder == null) return;
            var now = DateTimeOffset.UtcNow;
            long second = now.ToUnixTimeSeconds();
            if (second == _lastActivitySampleSecond) return;
            _lastActivitySampleSecond = second;
            TrainerData? trainer = now - _latestActivityTrainerAt <= TimeSpan.FromSeconds(3)
                ? _latestActivityTrainer : null;
            int? heartRate = now - _latestActivityHeartRateAt <= TimeSpan.FromSeconds(3)
                ? _latestActivityHeartRate : null;
            if (trainer == null && heartRate == null) return;
            double? power = trainer != null && double.IsFinite(trainer.PowerWatts) && trainer.PowerWatts >= 0
                ? trainer.PowerWatts : null;
            double? cadence = trainer?.CadenceRpm is double rpm && double.IsFinite(rpm) && rpm >= 0
                ? rpm : null;
            double? speed = trainer?.SpeedKph is double kph && double.IsFinite(kph) && kph >= 0
                ? kph : null;
            if (power == null && cadence == null && speed == null && heartRate == null) return;
            bool ok = _activityRecorder.Add(new ActivityEntry("sample", now,
                PowerWatts: power, CadenceRpm: cadence, SpeedKph: speed,
                HeartRateBpm: heartRate));
            if (!ok) ShowActivityError();
        }

        private void ShowActivityError()
        {
            if (_activityErrorShown) return;
            _activityErrorShown = true;
            _activityStatus.Text = $"Activity recording failed: {_activityRecorder?.Error}. " +
                $"Journal: {_lastActivityPath}";
        }

        private async Task FinishActivityAsync(ActivityRecorder recorder, string reason, double activeSeconds)
        {
            try
            {
                _activityStatus.Text = $"Finishing FIT activity: {recorder.JournalPath}";
                string fitPath = await recorder.FinishAsync(DateTimeOffset.UtcNow, activeSeconds, reason);
                _lastActivityPath = fitPath;
                if (!_closingForCleanup && _activityRecorder == null)
                    _activityStatus.Text = $"FIT activity ready: {fitPath}";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ActivityRecorder] FIT export failed: {ex}");
                _lastActivityPath = recorder.JournalPath;
                if (!_closingForCleanup && _activityRecorder == null)
                    _activityStatus.Text = $"FIT export failed: {ex.Message}. Journal: {recorder.JournalPath}";
            }
        }

        private void OpenActivityFolder()
        {
            if (_lastActivityPath == null) return;
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = Path.GetDirectoryName(_lastActivityPath)!, UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Unable to open activity folder: {ex.Message}", "Activity folder",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void StopSession()
            => StopSession("stopped");

        private void StopSession(string reason)
        {
            if (_activityRecorder is { } recorder)
            {
                _activityRecorder = null;
                _activityFinalization = FinishActivityAsync(recorder, reason, _sessionClock.Elapsed.TotalSeconds);
            }
            _sessionEventLog.Write("session_stopped");
            _sessionEventLog.Stop();
            _reconnectCancellation?.Cancel();
            _sessionRecovering = false;
            _trainerDeviceForRecovery = null;
            _btnRetryTacx.Enabled = false;
            _btnRetryTacx.Visible = false;
            ++_sessionGeneration;
            _sessionCancellation?.Cancel();
            _sessionCancellation?.Dispose();
            _sessionCancellation = null;
            _sessionTicker.Stop();
            _sessionClock.Stop();
            _runningSession = null;
            _lastCommandedBlock = -1;
            _sessionCommandPending = false;
            _trainingTimer.Enabled = true;
            _btnSetTargetPower.Enabled = _btnDisconnectTacx.Enabled &&
                _tacxTrainer.ControlMethod == "Tacx FE-C over BLE";
            _btnTestSlope.Enabled = _btnSetTargetPower.Enabled && !_manualControlPending;
            _chartPower.StopRecording();
            _chartCadence.StopRecording();
            _chartSpeed.StopRecording();
            _sessionBuilder.SetRunState(false, false, "Session stopped. Trainer retains its last target until changed or disconnected.");
        }

        protected override async void OnFormClosing(FormClosingEventArgs e)
        {
            if (_cleanupFinished)
            {
                base.OnFormClosing(e);
                return;
            }
            e.Cancel = true;
            if (_closingForCleanup) return;
            _closingForCleanup = true;
            Enabled = false;
            _reconnectCancellation?.Cancel();
            _manualControlCancellation?.Cancel();
            if (_sessionClock.IsActive || _sessionCommandPending) StopSession("closed");
            if (_activityFinalization != null) await _activityFinalization;
            if (_connectedTrainerDevice != null)
                _connectedTrainerDevice.GattServerDisconnected -= TrainerDevice_GattServerDisconnected;
            _connectedTrainerDevice = null;
            _sessionTicker.Stop();
            _sessionTicker.Tick -= SessionTicker_Tick;
            _sessionBuilder.StartRequested -= StartSessionAsync;
            _sessionBuilder.PauseRequested -= PauseSession;
            _sessionBuilder.ResumeRequested -= ResumeSession;
            _sessionBuilder.StopRequested -= StopSession;
            _trainingTimer.Started -= TrainingTimer_Started;
            _trainingTimer.Paused -= TrainingTimer_Paused;
            _trainingTimer.Stopped -= TrainingTimer_Stopped;
            try { _signalRecorder.Dispose(); }
            catch (Exception ex) { Debug.WriteLine($"[MainForm] Recorder cleanup failed: {ex}"); }
            _sessionEventLog.Dispose();
            _tacxTrainer.DataUpdated -= TacxTrainer_DataUpdated;
            _tacxTrainer.RawDataReceived -= Device_RawDataReceived;
            _hrmSensor.HeartRateReceived -= HrmSensor_HeartRateReceived;
            _hrmSensor.RawDataReceived -= Device_RawDataReceived;
            try { await _tacxTrainer.DisconnectAsync(); }
            catch (Exception ex) { Debug.WriteLine($"[MainForm] Trainer cleanup failed: {ex}"); }
            try { await _hrmSensor.DisconnectAsync(); }
            catch (Exception ex) { Debug.WriteLine($"[MainForm] HRM cleanup failed: {ex}"); }
            _cleanupFinished = true;
            if (!IsDisposed) Close();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _sessionTicker.Dispose();
            base.OnFormClosed(e);
        }
    }
}
