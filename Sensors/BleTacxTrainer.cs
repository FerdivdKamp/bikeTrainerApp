using InTheHand.Bluetooth;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ErgTrainer.Sensors
{
    /// <summary>
    /// BLE Tacx trainer (Neo or similar) using FTMS (Fitness Machine Service).
    /// </summary>
    public class BleTacxTrainer
    {
        // Fitness Machine Service UUID
        private static readonly Guid FitnessMachineServiceUuid =
            Guid.Parse("00001826-0000-1000-8000-00805f9b34fb");
        
        // Cycling Power Service UUID (alternative for Tacx Neo)
        private static readonly Guid CyclingPowerServiceUuid =
            Guid.Parse("00001818-0000-1000-8000-00805f9b34fb");
        
        // Indoor Bike Data characteristic UUID (FTMS)
        private static readonly Guid IndoorBikeDataUuid =
            Guid.Parse("00002ad2-0000-1000-8000-00805f9b34fb");
        private static readonly Guid FitnessMachineControlPointUuid =
            Guid.Parse("00002ad9-0000-1000-8000-00805f9b34fb");
        
        // Cycling Power Measurement characteristic UUID
        private static readonly Guid CyclingPowerMeasurementUuid =
            Guid.Parse("00002a63-0000-1000-8000-00805f9b34fb");

        // Cycling Speed and Cadence Service and CSC Measurement characteristic
        private static readonly Guid CyclingSpeedCadenceServiceUuid =
            Guid.Parse("00001816-0000-1000-8000-00805f9b34fb");
        private static readonly Guid CscMeasurementUuid =
            Guid.Parse("00002a5b-0000-1000-8000-00805f9b34fb");

        // Tacx FE-C over BLE uses the Tacx UART service, not the FTMS control point.
        private static readonly Guid FecServiceUuid = Guid.Parse("6e40fec1-b5a3-f393-e0a9-e50e24dcca9e");
        private static readonly Guid FecNotifyUuid = Guid.Parse("6e40fec2-b5a3-f393-e0a9-e50e24dcca9e");
        private static readonly Guid FecWriteUuid = Guid.Parse("6e40fec3-b5a3-f393-e0a9-e50e24dcca9e");

        public string Name => "BLE Tacx Trainer";

        public event EventHandler<TrainerData>? DataUpdated;
        public event EventHandler<RawDeviceData>? RawDataReceived;
        public bool HasCyclingSpeedCadenceNotifications => _cscMeasurementCharacteristic != null;
        public string ControlMethod { get; private set; } = "unavailable";
        public int WheelCircumferenceMm { get; set; } = 2105;
        public int? CharacteristicCount { get; private set; }
        public int OtherCharacteristicCount => _otherCharacteristics.Count;

        private BluetoothDevice? _device;
        private GattService? _ftmsService;
        private GattCharacteristic? _indoorBikeDataCharacteristic;
        private GattService? _cscService;
        private GattCharacteristic? _cscMeasurementCharacteristic;
        private GattService? _fecService;
        private GattCharacteristic? _fecNotifyCharacteristic;
        private GattCharacteristic? _fecWriteCharacteristic;
        private readonly SemaphoreSlim _controlGate = new(1, 1);
        private TaskCompletionSource<byte>? _pendingFecStatus;
        private CancellationTokenSource? _pollingCts;
        private readonly List<GattCharacteristic> _otherCharacteristics = new();
        private readonly object _measurementLock = new();
        private TrainerData? _latestPrimaryData;
        private (uint Revolutions, ushort Ticks)? _previousWheel;
        private (ushort Revolutions, ushort Ticks)? _previousCrank;
        private double? _cscSpeedKph;
        private double? _cscCadenceRpm;

        /// <summary>
        /// Checks if a device is a Tacx trainer by checking for the Fitness Machine Service.
        /// </summary>
        public static async Task<bool> IsTacxTrainerAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
        {
            if (device == null)
            {
                Debug.WriteLine("[Tacx] IsTacxTrainerAsync: device is null");
                return false;
            }

            Debug.WriteLine($"[Tacx] IsTacxTrainerAsync: checking device Name='{device.Name}', Id='{device.Id}'");

            try
            {
                // Get GATT instance
                var gatt = device.Gatt;
                
                // Try to connect without disconnecting first (disconnect might dispose it)
                Debug.WriteLine("[Tacx] Connecting GATT to check services…");
                try
                {
                    await gatt.ConnectAsync();
                }
                catch (ObjectDisposedException)
                {
                    Debug.WriteLine("[Tacx] GATT was disposed during ConnectAsync");
                    // Try to get a fresh GATT instance
                    try
                    {
                        gatt = device.Gatt;
                        await gatt.ConnectAsync();
                    }
                    catch
                    {
                        Debug.WriteLine("[Tacx] Failed to get fresh GATT instance");
                        return false;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Tacx] ConnectAsync failed: {ex.Message}");
                    return false;
                }
                Debug.WriteLine("[Tacx] GATT connected.");

                // Try to get all services to see what's available
                Debug.WriteLine("[Tacx] Discovering services…");
                var services = await gatt.GetPrimaryServicesAsync();
                Debug.WriteLine($"[Tacx] Found {services.Count} service(s)");
                foreach (var svc in services)
                {
                    Debug.WriteLine($"[Tacx]   Service UUID: {svc.Uuid}");
                }

                Debug.WriteLine("[Tacx] Checking for Fitness Machine Service…");
                var ftmsService = await gatt.GetPrimaryServiceAsync(FitnessMachineServiceUuid);
                
                if (ftmsService != null)
                {
                    Debug.WriteLine("[Tacx] Device is a Tacx trainer (FTMS).");
                    // Don't disconnect - leave connection open for ConnectToDeviceAsync to use
                    return true;
                }

                Debug.WriteLine("[Tacx] Checking for Cycling Power Service…");
                var cyclingPowerService = await gatt.GetPrimaryServiceAsync(CyclingPowerServiceUuid);
                
                if (cyclingPowerService != null)
                {
                    Debug.WriteLine("[Tacx] Device is a Tacx trainer (Cycling Power).");
                    // Don't disconnect - leave connection open for ConnectToDeviceAsync to use
                    return true;
                }

                Debug.WriteLine("[Tacx] Device does not have Fitness Machine Service or Cycling Power Service.");
                // Don't disconnect - leave connection open for HRM check to use
                // The HRM check will handle the connection
                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Tacx] IsTacxTrainerAsync failed: {ex.Message}");
                // Try to disconnect if we connected
                try
                {
                    device.Gatt.Disconnect();
                }
                catch { }
                return false;
            }
        }

        /// <summary>
        /// Connects to a specific Bluetooth device.
        /// </summary>
        public async Task<bool> ConnectToDeviceAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
        {
            if (device == null)
            {
                Debug.WriteLine("[Tacx] ConnectToDeviceAsync: device is null");
                return false;
            }

            Debug.WriteLine($"[Tacx] ConnectToDeviceAsync: connecting to device Name='{device.Name}', Id='{device.Id}'");

            _otherCharacteristics.Clear();
            CharacteristicCount = null;
            ResetMeasurements();
            ControlMethod = "unavailable";
            _device = device;

            var gatt = _device.Gatt;
            
            // Try to connect (might already be connected from IsTacxTrainerAsync)
            try
            {
                Debug.WriteLine("[Tacx] Connecting GATT…");
                await gatt.ConnectAsync();
                Debug.WriteLine("[Tacx] GATT connected.");
            }
            catch (ObjectDisposedException)
            {
                // GATT object was disposed, get a new one
                Debug.WriteLine("[Tacx] GATT was disposed, getting new GATT instance…");
                gatt = _device.Gatt;
                await gatt.ConnectAsync();
                Debug.WriteLine("[Tacx] GATT connected (new instance).");
            }
            catch (Exception ex)
            {
                // If already connected or other error, log and try to continue
                Debug.WriteLine($"[Tacx] GATT connection attempt: {ex.Message} - continuing anyway");
            }

            // Try FTMS first, then Cycling Power Service
            Debug.WriteLine("[Tacx] Getting Fitness Machine Service…");
            _ftmsService = await gatt.GetPrimaryServiceAsync(FitnessMachineServiceUuid);
            
            if (_ftmsService != null)
            {
                Debug.WriteLine("[Tacx] Using FTMS service.");
                Debug.WriteLine("[Tacx] Getting Indoor Bike Data characteristic…");
                _indoorBikeDataCharacteristic = await _ftmsService.GetCharacteristicAsync(IndoorBikeDataUuid);
                if (_indoorBikeDataCharacteristic == null)
                {
                    Debug.WriteLine("[Tacx] Indoor Bike Data characteristic not found.");
                    return false;
                }
            }
            else
            {
                // Try Cycling Power Service
                Debug.WriteLine("[Tacx] Getting Cycling Power Service…");
                var cyclingPowerService = await gatt.GetPrimaryServiceAsync(CyclingPowerServiceUuid);
                if (cyclingPowerService != null)
                {
                    Debug.WriteLine("[Tacx] Using Cycling Power Service.");
                    _ftmsService = cyclingPowerService; // Reuse the variable
                    Debug.WriteLine("[Tacx] Getting Cycling Power Measurement characteristic…");
                    _indoorBikeDataCharacteristic = await cyclingPowerService.GetCharacteristicAsync(CyclingPowerMeasurementUuid);
                    if (_indoorBikeDataCharacteristic == null)
                    {
                        Debug.WriteLine("[Tacx] Cycling Power Measurement characteristic not found.");
                        return false;
                    }
                }
                else
                {
                    Debug.WriteLine("[Tacx] Neither FTMS nor Cycling Power Service found.");
                    return false;
                }
            }

            Debug.WriteLine("[Tacx] Starting notifications (if supported)...");
            try
            {
                await _indoorBikeDataCharacteristic.StartNotificationsAsync();
                Debug.WriteLine("[Tacx] Notifications started.");
                
                // Subscribe to notification events
                _indoorBikeDataCharacteristic.CharacteristicValueChanged += IndoorBikeDataCharacteristic_ValueChanged;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Tacx] StartNotificationsAsync failed (ignored): {ex.Message}");
            }

            // Capture the standard speed/cadence characteristic separately when available.
            try
            {
                _cscService = await gatt.GetPrimaryServiceAsync(CyclingSpeedCadenceServiceUuid);
                if (_cscService != null)
                {
                    var characteristic = await _cscService.GetCharacteristicAsync(CscMeasurementUuid);
                    if (characteristic != null)
                    {
                        characteristic.CharacteristicValueChanged += CscMeasurementCharacteristic_ValueChanged;
                        try
                        {
                            await characteristic.StartNotificationsAsync();
                            _cscMeasurementCharacteristic = characteristic;
                            Debug.WriteLine("[Tacx] CSC Measurement notifications started.");
                        }
                        catch
                        {
                            characteristic.CharacteristicValueChanged -= CscMeasurementCharacteristic_ValueChanged;
                            throw;
                        }
                    }
                    else
                    {
                        Debug.WriteLine("[Tacx] CSC Measurement characteristic not found.");
                    }
                }
                else
                {
                    Debug.WriteLine("[Tacx] Cycling Speed and Cadence Service not found.");
                }
            }
            catch (Exception ex)
            {
                _cscService = null;
                Debug.WriteLine($"[Tacx] CSC notifications unavailable: {ex.Message}");
            }

            await DiscoverControlAsync(gatt);

            await DiscoverOtherCharacteristicsAsync(gatt, cancellationToken);

            Debug.WriteLine("[Tacx] Starting polling loop (as backup)...");
            _pollingCts = new CancellationTokenSource();
            _ = Task.Run(() => PollLoopAsync(_pollingCts.Token), _pollingCts.Token);

            Debug.WriteLine("[Tacx] ConnectToDeviceAsync: success");
            return true;
        }

        public async Task DisconnectAsync()
        {
            await _controlGate.WaitAsync();
            try
            {
                _pendingFecStatus?.TrySetCanceled();
                _pendingFecStatus = null;
                if (_fecNotifyCharacteristic != null)
                {
                    _fecNotifyCharacteristic.CharacteristicValueChanged -= FecNotifyCharacteristic_ValueChanged;
                    try
                    {
                        await _fecNotifyCharacteristic.StopNotificationsAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Tacx] Unable to stop FE-C notifications: {ex.Message}");
                    }
                }
                _fecNotifyCharacteristic = null;
                _fecWriteCharacteristic = null;
                _fecService = null;
                ControlMethod = "unavailable";

                // stop our polling loop
                _pollingCts?.Cancel();
                _pollingCts = null;
                _otherCharacteristics.Clear();
                CharacteristicCount = null;
                ResetMeasurements();

                if (_cscMeasurementCharacteristic != null)
                {
                    try
                    {
                        _cscMeasurementCharacteristic.CharacteristicValueChanged -= CscMeasurementCharacteristic_ValueChanged;
                        await _cscMeasurementCharacteristic.StopNotificationsAsync();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[Tacx] Unable to stop CSC notifications: {ex.Message}");
                    }

                    _cscMeasurementCharacteristic = null;
                }
                _cscService = null;

                if (_indoorBikeDataCharacteristic != null)
                {
                    try
                    {
                        _indoorBikeDataCharacteristic.CharacteristicValueChanged -= IndoorBikeDataCharacteristic_ValueChanged;
                        await _indoorBikeDataCharacteristic.StopNotificationsAsync();
                    }
                    catch
                    {
                        // ignore errors on shutdown
                    }

                    _indoorBikeDataCharacteristic = null;
                }

                // we're done with the service too
                _ftmsService = null;

                // End the GATT session to release trainer control.
                try
                {
                    _device?.Gatt.Disconnect();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Tacx] Unable to disconnect GATT: {ex.Message}");
                }

                _device = null;
            }
            finally
            {
                _controlGate.Release();
            }
        }

        private async Task DiscoverControlAsync(RemoteGattServer gatt)
        {
            try
            {
                _fecService = await gatt.GetPrimaryServiceAsync(FecServiceUuid);
                if (_fecService != null)
                {
                    var notify = await _fecService.GetCharacteristicAsync(FecNotifyUuid);
                    var write = await _fecService.GetCharacteristicAsync(FecWriteUuid);
                    if (notify != null && write != null &&
                        (notify.Properties & GattCharacteristicProperties.Notify) != 0 &&
                        (write.Properties & (GattCharacteristicProperties.WriteWithoutResponse | GattCharacteristicProperties.Write)) != 0)
                    {
                        notify.CharacteristicValueChanged += FecNotifyCharacteristic_ValueChanged;
                        try
                        {
                            await notify.StartNotificationsAsync();
                            _fecNotifyCharacteristic = notify;
                            _fecWriteCharacteristic = write;
                            ControlMethod = "Tacx FE-C over BLE";
                            return;
                        }
                        catch
                        {
                            notify.CharacteristicValueChanged -= FecNotifyCharacteristic_ValueChanged;
                            throw;
                        }
                    }
                    Debug.WriteLine("[Tacx] FE-C service lacks usable notify/write characteristics.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Tacx] FE-C control unavailable: {ex.Message}");
            }

            _fecService = null;
            if (_ftmsService?.Uuid == FitnessMachineServiceUuid)
            {
                try
                {
                    var controlPoint = await _ftmsService.GetCharacteristicAsync(FitnessMachineControlPointUuid);
                    if (controlPoint != null &&
                        (controlPoint.Properties & GattCharacteristicProperties.Write) != 0 &&
                        (controlPoint.Properties & GattCharacteristicProperties.Indicate) != 0)
                    {
                        ControlMethod = "FTMS";
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Tacx] FTMS control discovery unavailable: {ex.Message}");
                }
            }
        }

        public async Task<string> SetTargetPowerAsync(int watts, CancellationToken cancellationToken = default)
        {
            if (watts < 0 || watts > 1000)
            {
                throw new ArgumentOutOfRangeException(nameof(watts), "Target power must be between 0 and 1000 W.");
            }
            if (!await _controlGate.WaitAsync(0, cancellationToken))
            {
                return "A trainer control operation is already in progress.";
            }

            try
            {
                var write = _fecWriteCharacteristic;
                if (_device == null || write == null || _fecNotifyCharacteristic == null)
                {
                    return "Tacx FE-C over BLE control is unavailable on this connection.";
                }

                // FE-C acknowledged data page 49: target power in quarter-watt units.
                ushort quarterWatts = (ushort)(watts * 4);
                byte[] command = { 0xA4, 0x09, 0x4F, 0x05, 0x31,
                    0xFF, 0xFF, 0xFF, 0xFF, 0xFF,
                    (byte)quarterWatts, (byte)(quarterWatts >> 8), 0x00 };
                for (int i = 0; i < command.Length - 1; i++)
                {
                    command[12] ^= command[i];
                }

                var response = new TaskCompletionSource<byte>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pendingFecStatus = response;
                cancellationToken.ThrowIfCancellationRequested();
                if ((write.Properties & GattCharacteristicProperties.WriteWithoutResponse) != 0)
                {
                    await write.WriteValueWithoutResponseAsync(command);
                }
                else
                {
                    await write.WriteValueWithResponseAsync(command);
                }

                byte status = await response.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                return status switch
                {
                    0 => $"Trainer accepted {watts} W ERG target.",
                    1 => "Trainer reported that the ERG command failed. Another app may own control.",
                    2 => "Trainer does not support the ERG command.",
                    3 => "Trainer rejected the ERG command. Another app may own control.",
                    4 => "Trainer is still processing the ERG command.",
                    _ => $"Trainer returned unknown FE-C command status 0x{status:X2}."
                };
            }
            finally
            {
                _pendingFecStatus = null;
                _controlGate.Release();
            }
        }

        private void FecNotifyCharacteristic_ValueChanged(object? sender, GattCharacteristicValueChangedEventArgs e)
        {
            if (e.Error != null)
            {
                _pendingFecStatus?.TrySetException(new InvalidOperationException($"FE-C notification failed: {e.Error}"));
                return;
            }

            byte[]? data = e.Value;
            if (data == null || data.Length != 13 || data[0] != 0xA4 || data[1] != 0x09 || data[3] != 0x05)
            {
                return;
            }

            byte checksum = 0;
            foreach (byte value in data)
            {
                checksum ^= value;
            }
            if (checksum != 0)
            {
                Debug.WriteLine("[Tacx] Ignoring FE-C packet with invalid checksum.");
                return;
            }

            // Command Status page 71: last command, sequence, result, then four data bytes.
            if (data[4] == 0x47 && data[5] == 0x31)
            {
                Debug.WriteLine($"[Tacx] FE-C ERG command status: {Convert.ToHexString(data)}");
                _pendingFecStatus?.TrySetResult(data[7]);
            }
        }

        private async Task DiscoverOtherCharacteristicsAsync(RemoteGattServer gatt, CancellationToken token)
        {
            _otherCharacteristics.Clear();
            CharacteristicCount = null;
            int count = 0;
            bool complete = true;

            try
            {
                var services = await gatt.GetPrimaryServicesAsync();
                foreach (var service in services)
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        var characteristics = await service.GetCharacteristicsAsync();
                        foreach (var characteristic in characteristics)
                        {
                            count++;
                            if ((service.Uuid == _ftmsService?.Uuid && characteristic.Uuid == _indoorBikeDataCharacteristic?.Uuid) ||
                                (service.Uuid == CyclingSpeedCadenceServiceUuid && characteristic.Uuid == CscMeasurementUuid))
                            {
                                continue;
                            }

                            _otherCharacteristics.Add(characteristic);
                            Debug.WriteLine($"[Tacx] GATT {service.Uuid}/{characteristic.Uuid}: {characteristic.Properties}");
                        }
                    }
                    catch (Exception ex)
                    {
                        complete = false;
                        Debug.WriteLine($"[Tacx] Unable to inspect service {service.Uuid}: {ex.Message}");
                    }
                }

                if (complete)
                {
                    CharacteristicCount = count;
                    Debug.WriteLine($"[Tacx] Found {count} characteristic(s), {_otherCharacteristics.Count} additional.");
                }
                else
                {
                    _otherCharacteristics.Clear();
                    Debug.WriteLine("[Tacx] GATT characteristic inventory incomplete.");
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _otherCharacteristics.Clear();
                Debug.WriteLine($"[Tacx] GATT characteristic discovery unavailable: {ex.Message}");
            }
        }

        public async Task<int> SampleOtherCharacteristicsAsync(CancellationToken cancellationToken = default)
        {
            int sampled = 0;
            foreach (var characteristic in _otherCharacteristics.ToArray())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if ((characteristic.Properties & GattCharacteristicProperties.Read) == 0)
                {
                    continue;
                }

                try
                {
                    var value = await characteristic.ReadValueAsync();
                    if (value != null && _device != null)
                    {
                        RawDataReceived?.Invoke(this, new RawDeviceData(
                            DateTimeOffset.UtcNow,
                            "trainer",
                            _device?.Name ?? "Tacx Trainer",
                            "sample",
                            characteristic.Service.Uuid,
                            characteristic.Uuid,
                            (byte[])value.Clone()));
                        sampled++;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Tacx] Unable to sample {characteristic.Uuid}: {ex.Message}");
                }
            }

            return sampled;
        }

        public async Task<string> TestFtmsControlAsync(CancellationToken cancellationToken = default)
        {
            if (!await _controlGate.WaitAsync(0, cancellationToken))
            {
                return "A trainer control operation is already in progress.";
            }

            try
            {
                return await TestFtmsControlCoreAsync(cancellationToken);
            }
            finally
            {
                _controlGate.Release();
            }
        }

        private async Task<string> TestFtmsControlCoreAsync(CancellationToken cancellationToken)
        {
            if (_device == null || _ftmsService?.Uuid != FitnessMachineServiceUuid)
            {
                return "FTMS control is unavailable on this connection.";
            }

            var controlPoint = await _ftmsService.GetCharacteristicAsync(FitnessMachineControlPointUuid);
            if (controlPoint == null ||
                (controlPoint.Properties & GattCharacteristicProperties.Write) == 0 ||
                (controlPoint.Properties & GattCharacteristicProperties.Indicate) == 0)
            {
                return "FTMS control point with write and indication support was not found.";
            }

            var response = new TaskCompletionSource<byte[]>(TaskCreationOptions.RunContinuationsAsynchronously);
            void OnResponse(object? sender, GattCharacteristicValueChangedEventArgs e)
            {
                if (e.Error != null)
                {
                    response.TrySetException(new InvalidOperationException($"Control point indication failed: {e.Error}"));
                    return;
                }

                byte[]? value = e.Value;
                if (value != null && value.Length >= 3 && value[0] == 0x80 && value[1] == 0x00)
                {
                    response.TrySetResult((byte[])value.Clone());
                }
            }

            controlPoint.CharacteristicValueChanged += OnResponse;
            try
            {
                await controlPoint.StartNotificationsAsync();
                cancellationToken.ThrowIfCancellationRequested();
                await controlPoint.WriteValueWithResponseAsync(new byte[] { 0x00 });
                byte[] reply = await response.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
                string result = reply[2] switch
                {
                    0x01 => "Success: trainer granted control.",
                    0x02 => "Trainer rejected the request: command unsupported.",
                    0x03 => "Trainer rejected the request: invalid parameter.",
                    0x04 => "Trainer rejected the request: operation failed.",
                    0x05 => "Trainer rejected the request: control not permitted.",
                    _ => $"Trainer returned result code 0x{reply[2]:X2}."
                };
                return $"{result} Raw response: {Convert.ToHexString(reply)}";
            }
            finally
            {
                controlPoint.CharacteristicValueChanged -= OnResponse;
                try
                {
                    await controlPoint.StopNotificationsAsync();
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Tacx] Unable to stop control point indications: {ex.Message}");
                }
            }
        }

        private void CscMeasurementCharacteristic_ValueChanged(object? sender, GattCharacteristicValueChangedEventArgs e)
        {
            try
            {
                byte[]? data = e.Value;
                if (data != null && data.Length > 0)
                {
                    RawDataReceived?.Invoke(this, new RawDeviceData(
                        DateTimeOffset.UtcNow,
                        "trainer",
                        _device?.Name ?? "Tacx Trainer",
                        "notification",
                        CyclingSpeedCadenceServiceUuid,
                        CscMeasurementUuid,
                        (byte[])data.Clone()));
                    var trainerData = ParseCscMeasurement(data);
                    if (trainerData != null)
                    {
                        DataUpdated?.Invoke(this, trainerData);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Tacx] CSC notification handler error: {ex.Message}");
            }
        }

        private void ResetMeasurements()
        {
            lock (_measurementLock)
            {
                _latestPrimaryData = null;
                _previousWheel = null;
                _previousCrank = null;
                _cscSpeedKph = null;
                _cscCadenceRpm = null;
            }
        }

        private TrainerData UpdatePrimaryMeasurement(TrainerData data)
        {
            lock (_measurementLock)
            {
                _latestPrimaryData = data;
                return data with
                {
                    SpeedKph = _cscSpeedKph ?? data.SpeedKph,
                    CadenceRpm = _cscCadenceRpm ?? data.CadenceRpm
                };
            }
        }

        private TrainerData? ParseCscMeasurement(byte[] data)
        {
            byte flags = data[0];
            if ((flags & ~0x03) != 0 || data.Length < 1 + ((flags & 1) != 0 ? 6 : 0) + ((flags & 2) != 0 ? 4 : 0))
            {
                Debug.WriteLine("[Tacx] Invalid CSC measurement.");
                return null;
            }

            int offset = 1;
            lock (_measurementLock)
            {
                if ((flags & 1) != 0)
                {
                    uint revolutions = BitConverter.ToUInt32(data, offset);
                    ushort ticks = BitConverter.ToUInt16(data, offset + 4);
                    if (_previousWheel is { } previous)
                    {
                        uint delta = unchecked(revolutions - previous.Revolutions);
                        ushort elapsed = unchecked((ushort)(ticks - previous.Ticks));
                        _cscSpeedKph = delta == 0 ? 0 : elapsed > 0
                            ? delta * WheelCircumferenceMm * 0.0036 * 1024 / elapsed
                            : null;
                    }
                    _previousWheel = (revolutions, ticks);
                    offset += 6;
                }
                else
                {
                    _previousWheel = null;
                    _cscSpeedKph = null;
                }

                if ((flags & 2) != 0)
                {
                    ushort revolutions = BitConverter.ToUInt16(data, offset);
                    ushort ticks = BitConverter.ToUInt16(data, offset + 2);
                    if (_previousCrank is { } previous)
                    {
                        ushort delta = unchecked((ushort)(revolutions - previous.Revolutions));
                        ushort elapsed = unchecked((ushort)(ticks - previous.Ticks));
                        _cscCadenceRpm = delta == 0 ? 0 : elapsed > 0
                            ? delta * 60.0 * 1024 / elapsed
                            : null;
                    }
                    _previousCrank = (revolutions, ticks);
                }
                else
                {
                    _previousCrank = null;
                    _cscCadenceRpm = null;
                }

                return _latestPrimaryData is { } primary ? primary with
                {
                    SpeedKph = _cscSpeedKph ?? primary.SpeedKph,
                    CadenceRpm = _cscCadenceRpm ?? primary.CadenceRpm
                } : null;
            }
        }

        private void IndoorBikeDataCharacteristic_ValueChanged(object? sender, GattCharacteristicValueChangedEventArgs e)
        {
            try
            {
                byte[]? data = e.Value;
                Debug.WriteLine($"[Tacx] Notification received: {data?.Length ?? 0} bytes");
                if (data != null && data.Length > 0)
                {
                    Debug.WriteLine($"[Tacx] Data: {BitConverter.ToString(data)}");
                    PublishRawData(data, "notification");
                    
                }

                if (data != null && data.Length >= 2)
                {
                    // Parse FTMS Indoor Bike Data or Cycling Power Data
                    var trainerData = ParseIndoorBikeData(data);
                    if (trainerData != null)
                    {
                        trainerData = UpdatePrimaryMeasurement(trainerData);
                        Debug.WriteLine($"[Tacx] Parsed: Power={trainerData.PowerWatts:F0}W, Cadence={trainerData.CadenceRpm:F0}rpm, Speed={trainerData.SpeedKph:F1}kph");
                        DataUpdated?.Invoke(this, trainerData);
                    }
                    else
                    {
                        Debug.WriteLine("[Tacx] Failed to parse data");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Tacx] Notification handler error: {ex.Message}");
            }
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            if (_indoorBikeDataCharacteristic == null)
                return;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    byte[]? data = await _indoorBikeDataCharacteristic.ReadValueAsync();
                    Debug.WriteLine($"[Tacx] Poll read: {data?.Length ?? 0} bytes");
                    if (data != null && data.Length > 0)
                    {
                        Debug.WriteLine($"[Tacx] Poll data: {BitConverter.ToString(data)}");
                        PublishRawData(data, "poll");
                        
                    }

                    if (data != null && data.Length >= 2)
                    {
                        // Parse FTMS Indoor Bike Data or Cycling Power Data
                        var trainerData = ParseIndoorBikeData(data);
                        if (trainerData != null)
                        {
                            trainerData = UpdatePrimaryMeasurement(trainerData);
                            Debug.WriteLine($"[Tacx] Poll parsed: Power={trainerData.PowerWatts:F0}W, Cadence={trainerData.CadenceRpm:F0}rpm, Speed={trainerData.SpeedKph:F1}kph");
                            DataUpdated?.Invoke(this, trainerData);
                        }
                        else
                        {
                            Debug.WriteLine("[Tacx] Poll failed to parse data");
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    // Log read errors for debugging
                    Debug.WriteLine($"[Tacx] PollLoopAsync read error: {ex.Message}");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1), token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }

        private void PublishRawData(byte[] data, string deliveryMethod)
        {
            RawDataReceived?.Invoke(this, new RawDeviceData(
                DateTimeOffset.UtcNow,
                "trainer",
                _device?.Name ?? "Tacx Trainer",
                deliveryMethod,
                _ftmsService?.Uuid ?? Guid.Empty,
                _indoorBikeDataCharacteristic?.Uuid ?? Guid.Empty,
                (byte[])data.Clone()));
        }

        private TrainerData? ParseIndoorBikeData(byte[] data)
        {
            // The connected characteristic identifies the wire format. Cycling Power flags
            // can start with the same bytes as FTMS flags.
            if (_indoorBikeDataCharacteristic?.Uuid == CyclingPowerMeasurementUuid)
            {
                return ParseCyclingPowerData(data);
            }

            if (_indoorBikeDataCharacteristic?.Uuid == IndoorBikeDataUuid)
            {
                return ParseFtmsData(data);
            }

            return null;
        }

        private TrainerData? ParseFtmsData(byte[] data)
        {
            // FTMS Indoor Bike Data parser
            // According to Bluetooth SIG FTMS specification:
            // Format: Flags (1 byte) + [optional fields in specific order based on flags]
            // Field order when present:
            // 1. Instantaneous Speed (if More Data flag is set)
            // 2. Average Speed (if Average Speed Present flag is set)
            // 3. Instantaneous Cadence (if Instantaneous Cadence Present flag is set)
            // 4. Average Cadence (if Average Cadence Present flag is set)
            // 5. Total Distance (if Total Distance Present flag is set)
            // 6. Resistance Level (if Resistance Level Present flag is set)
            // 7. Instantaneous Power (if Instantaneous Power Present flag is set)
            // 8. Average Power (if Average Power Present flag is set)
            if (data.Length < 2) return null;

            // Flags byte
            byte flags = data[0];
            bool moreData = (flags & 0x01) != 0;  // Bit 0: More Data (Instantaneous Speed)
            bool averageSpeedPresent = (flags & 0x02) != 0;  // Bit 1: Average Speed Present
            bool instantaneousCadencePresent = (flags & 0x04) != 0;  // Bit 2: Instantaneous Cadence Present
            bool averageCadencePresent = (flags & 0x08) != 0;  // Bit 3: Average Cadence Present
            bool totalDistancePresent = (flags & 0x10) != 0;  // Bit 4: Total Distance Present
            bool resistanceLevelPresent = (flags & 0x20) != 0;  // Bit 5: Resistance Level Present
            bool instantaneousPowerPresent = (flags & 0x40) != 0;  // Bit 6: Instantaneous Power Present
            bool averagePowerPresent = (flags & 0x80) != 0;  // Bit 7: Average Power Present

            Debug.WriteLine($"[Tacx] FTMS Flags: 0x{flags:X2} - moreData={moreData}, avgSpeed={averageSpeedPresent}, instCad={instantaneousCadencePresent}, instPower={instantaneousPowerPresent}, dataLength={data.Length}");
            Debug.WriteLine($"[Tacx] FTMS Raw data: {BitConverter.ToString(data)}");

            int offset = 1;
            double speedKph = 0;
            double cadenceRpm = 0;
            double powerWatts = 0;

            // Parse fields in the correct order according to FTMS specification
            // 1. Instantaneous Speed (if More Data flag is set)
            if (moreData && data.Length >= offset + 2)
            {
                ushort speed = BitConverter.ToUInt16(data, offset);
                // Try different interpretations
                double testSpeed1 = speed * 0.01;  // Standard FTMS (resolution 0.01)
                double testSpeed2 = speed * 0.001; // Alternative resolution
                // Try byte-swapped (big-endian interpretation)
                ushort speedSwapped = (ushort)((speed >> 8) | (speed << 8));
                double testSpeed3 = speedSwapped * 0.01;
                double testSpeed4 = speedSwapped * 0.001;
                double testSpeed5 = speedSwapped; // Direct value (no multiplier)
                
                // Use the interpretation that gives a reasonable speed (0-50 kph)
                // Prefer byte-swapped with standard multiplier if it's reasonable
                if (testSpeed3 >= 0 && testSpeed3 <= 50)
                {
                    speedKph = testSpeed3;
                    Debug.WriteLine($"[Tacx] Instantaneous Speed @{offset}: {speed} (0x{speed:X4}) swapped={speedSwapped} *0.01 -> {speedKph:F2} kph");
                }
                else if (testSpeed5 >= 0 && testSpeed5 <= 50)
                {
                    speedKph = testSpeed5;
                    Debug.WriteLine($"[Tacx] Instantaneous Speed @{offset}: {speed} (0x{speed:X4}) swapped={speedSwapped} direct -> {speedKph:F2} kph");
                }
                else if (testSpeed1 >= 0 && testSpeed1 <= 50)
                {
                    speedKph = testSpeed1;
                    Debug.WriteLine($"[Tacx] Instantaneous Speed @{offset}: {speed} (0x{speed:X4}) *0.01 -> {speedKph:F2} kph");
                }
                else if (testSpeed2 >= 0 && testSpeed2 <= 50)
                {
                    speedKph = testSpeed2;
                    Debug.WriteLine($"[Tacx] Instantaneous Speed @{offset}: {speed} (0x{speed:X4}) *0.001 -> {speedKph:F2} kph");
                }
                else if (testSpeed4 >= 0 && testSpeed4 <= 50)
                {
                    speedKph = testSpeed4;
                    Debug.WriteLine($"[Tacx] Instantaneous Speed @{offset}: {speed} (0x{speed:X4}) swapped={speedSwapped} *0.001 -> {speedKph:F2} kph");
                }
                else
                {
                    // If none are reasonable, use byte-swapped with standard multiplier as fallback
                    speedKph = testSpeed3;
                    Debug.WriteLine($"[Tacx] Instantaneous Speed @{offset}: {speed} (0x{speed:X4}) -> {speedKph:F2} kph (tried: *0.01={testSpeed1:F2}, *0.001={testSpeed2:F2}, swapped*0.01={testSpeed3:F2}, swapped*0.001={testSpeed4:F2}, swapped direct={testSpeed5:F2})");
                }
                offset += 2;
            }

            // 2. Average Speed (if Average Speed Present flag is set)
            if (averageSpeedPresent && data.Length >= offset + 2)
            {
                // Skip average speed - we use instantaneous speed
                offset += 2;
                Debug.WriteLine($"[Tacx] Average Speed skipped @{offset - 2}");
            }

            // 3. Instantaneous Cadence (if Instantaneous Cadence Present flag is set)
            if (instantaneousCadencePresent && data.Length >= offset + 2)
            {
                ushort cadence = BitConverter.ToUInt16(data, offset);
                cadenceRpm = cadence * 0.5; // Cadence in rpm with resolution 0.5
                Debug.WriteLine($"[Tacx] Instantaneous Cadence @{offset}: {cadence} -> {cadenceRpm:F1} rpm");
                offset += 2;
            }

            // 4. Average Cadence (if Average Cadence Present flag is set)
            if (averageCadencePresent && data.Length >= offset + 2)
            {
                // Skip average cadence - we use instantaneous cadence
                offset += 2;
                Debug.WriteLine($"[Tacx] Average Cadence skipped @{offset - 2}");
            }

            // 5. Total Distance (if Total Distance Present flag is set) - 3 bytes
            if (totalDistancePresent && data.Length >= offset + 3)
            {
                offset += 3;
                Debug.WriteLine($"[Tacx] Total Distance skipped @{offset - 3}");
            }

            // 6. Resistance Level (if Resistance Level Present flag is set)
            if (resistanceLevelPresent && data.Length >= offset + 2)
            {
                offset += 2;
                Debug.WriteLine($"[Tacx] Resistance Level skipped @{offset - 2}");
            }

            // 7. Instantaneous Power (if Instantaneous Power Present flag is set)
            if (instantaneousPowerPresent && data.Length >= offset + 2)
            {
                short power = BitConverter.ToInt16(data, offset);
                powerWatts = power; // Power in watts (signed 16-bit)
                Debug.WriteLine($"[Tacx] Instantaneous Power @{offset}: {power} -> {powerWatts:F0} W");
                offset += 2;
            }

            // 8. Average Power (if Average Power Present flag is set)
            if (averagePowerPresent && data.Length >= offset + 2)
            {
                // Skip average power - we use instantaneous power
                offset += 2;
                Debug.WriteLine($"[Tacx] Average Power skipped @{offset - 2}");
            }
            
            // Note: Tacx may send data even when flags don't indicate it's present
            // If we have more data than expected based on flags, try to find cadence and power
            // This handles cases where Tacx sends data in a non-standard way
            
            // If cadence and power are still 0, but we have extra data, try to find them
            // Based on observed data: 03-00-02-00-35-01-00-00-28-0A-3C-00-00-F0
            // Speed at 1-2, avg speed at 3-4, then there's more data
            // After speed fields, offset should be 5, so try cadence/power from there
            if ((cadenceRpm == 0 || powerWatts == 0) && data.Length > offset)
            {
                int remainingBytes = data.Length - offset;
                Debug.WriteLine($"[Tacx] Flags don't indicate cadence/power, but {remainingBytes} bytes remain (offset={offset}). Trying to find them...");
                
                // Try cadence at various positions after the parsed fields
                // Based on data: 03-00-24-00-70-01-00-00-9B-7D-55-00-3C-03
                // After speed (1-2) and avg speed (3-4), offset = 5
                // Try different interpretations at each position
                if (cadenceRpm == 0)
                {
                    for (int cadOffset = offset; cadOffset <= offset + 6 && cadOffset + 2 <= data.Length; cadOffset += 2)
                    {
                        ushort cadence = BitConverter.ToUInt16(data, cadOffset);
                        
                        // Try different interpretations
                        double testCadence1 = cadence * 0.5;  // FTMS standard
                        double testCadence2 = cadence;        // Direct value
                        double testCadence3 = cadence / 2.0;  // Alternative
                        
                        // Also try just the low byte
                        byte cadenceLow = data[cadOffset];
                        double testCadence4 = cadenceLow * 0.5;
                        double testCadence5 = cadenceLow;
                        
                        // Accept reasonable cadence values (5-200 rpm)
                        if (testCadence1 >= 5 && testCadence1 < 200)
                        {
                            cadenceRpm = testCadence1;
                            Debug.WriteLine($"[Tacx] Cadence found @{cadOffset}: {cadence} (0x{cadence:X4}) *0.5 -> {cadenceRpm:F1} rpm");
                            break;
                        }
                        else if (testCadence2 >= 5 && testCadence2 < 200)
                        {
                            cadenceRpm = testCadence2;
                            Debug.WriteLine($"[Tacx] Cadence found @{cadOffset}: {cadence} (0x{cadence:X4}) direct -> {cadenceRpm:F1} rpm");
                            break;
                        }
                        else if (testCadence4 >= 5 && testCadence4 < 200)
                        {
                            cadenceRpm = testCadence4;
                            Debug.WriteLine($"[Tacx] Cadence found @{cadOffset}: low byte {cadenceLow} (0x{cadenceLow:X2}) *0.5 -> {cadenceRpm:F1} rpm");
                            break;
                        }
                        else if (testCadence5 >= 5 && testCadence5 < 200)
                        {
                            cadenceRpm = testCadence5;
                            Debug.WriteLine($"[Tacx] Cadence found @{cadOffset}: low byte {cadenceLow} (0x{cadenceLow:X2}) direct -> {cadenceRpm:F1} rpm");
                            break;
                        }
                        else
                        {
                            Debug.WriteLine($"[Tacx] Cadence @{cadOffset}: {cadence} (0x{cadence:X4}) - *0.5={testCadence1:F1}, direct={testCadence2:F1}, low={cadenceLow} (rejected)");
                        }
                    }
                }
                
                // Try power at various positions
                // Based on data: 03-00-24-00-70-01-00-00-9B-7D-55-00-3C-03
                // Try offset 9-10 (9B-7D), 11-12 (55-00), 13-14 (3C-03)
                if (powerWatts == 0)
                {
                    for (int powOffset = offset + 4; powOffset <= offset + 8 && powOffset + 2 <= data.Length; powOffset += 2)
                    {
                        ushort powerRaw = BitConverter.ToUInt16(data, powOffset);
                        short powerSigned = BitConverter.ToInt16(data, powOffset);
                        
                        // Also try just the low byte
                        byte powerLow = data[powOffset];
                        byte powerHigh = data[powOffset + 1];
                        
                        // Try signed first (FTMS uses signed for power)
                        if (powerSigned >= 5 && powerSigned < 2000)
                        {
                            powerWatts = powerSigned;
                            Debug.WriteLine($"[Tacx] Power found @{powOffset}: {powerSigned} (0x{powerRaw:X4} signed) -> {powerWatts:F0} W");
                            break;
                        }
                        // Try unsigned
                        else if (powerRaw >= 5 && powerRaw < 2000)
                        {
                            powerWatts = powerRaw;
                            Debug.WriteLine($"[Tacx] Power found @{powOffset}: {powerRaw} (0x{powerRaw:X4} unsigned) -> {powerWatts:F0} W");
                            break;
                        }
                        // Try low byte only
                        else if (powerLow >= 5 && powerLow < 2000)
                        {
                            powerWatts = powerLow;
                            Debug.WriteLine($"[Tacx] Power found @{powOffset}: low byte {powerLow} (0x{powerLow:X2}) -> {powerWatts:F0} W");
                            break;
                        }
                        // Try high byte only
                        else if (powerHigh >= 5 && powerHigh < 2000)
                        {
                            powerWatts = powerHigh;
                            Debug.WriteLine($"[Tacx] Power found @{powOffset}: high byte {powerHigh} (0x{powerHigh:X2}) -> {powerWatts:F0} W");
                            break;
                        }
                        else
                        {
                            Debug.WriteLine($"[Tacx] Power @{powOffset}: {powerRaw} (0x{powerRaw:X4})/{powerSigned}, low={powerLow}, high={powerHigh} (rejected)");
                        }
                    }
                }
            }

            return new TrainerData(
                Connected: true,
                DeviceName: _device?.Name ?? "Tacx Trainer",
                PowerWatts: powerWatts,
                CadenceRpm: cadenceRpm,
                SpeedKph: speedKph
            );
        }

        private TrainerData? ParseCyclingPowerData(byte[] data)
        {
            // Cycling Power Measurement data parser
            // Format: Flags (2 bytes) + [optional fields based on flags]
            if (data.Length < 4) return null;

            ushort flags = BitConverter.ToUInt16(data, 0);
            bool pedalPowerBalancePresent = (flags & 0x01) != 0;
            bool pedalPowerBalanceReference = (flags & 0x02) != 0;
            bool accumulatedTorquePresent = (flags & 0x04) != 0;
            bool accumulatedTorqueSource = (flags & 0x08) != 0;
            bool wheelRevolutionDataPresent = (flags & 0x10) != 0;
            bool crankRevolutionDataPresent = (flags & 0x20) != 0;
            bool extremeForceMagnitudesPresent = (flags & 0x40) != 0;
            bool extremeTorqueMagnitudesPresent = (flags & 0x80) != 0;
            bool extremeAnglesPresent = (flags & 0x100) != 0;
            bool topDeadSpotAnglePresent = (flags & 0x200) != 0;
            bool bottomDeadSpotAnglePresent = (flags & 0x400) != 0;
            bool accumulatedEnergyPresent = (flags & 0x800) != 0;

            int offset = 2;
            double powerWatts = 0;
            // Instantaneous Power (always present, 2 bytes, signed)
            short power = BitConverter.ToInt16(data, offset);
            powerWatts = power;
            offset += 2;

            // Pedal Power Balance (if present)
            if (pedalPowerBalancePresent && data.Length >= offset + 1)
            {
                offset += 1;
            }

            // Accumulated Torque (if present)
            if (accumulatedTorquePresent && data.Length >= offset + 2)
            {
                offset += 2;
            }

            // Wheel Revolution Data (if present) - can calculate speed from this
            if (wheelRevolutionDataPresent && data.Length >= offset + 6)
            {
                uint cumulativeWheelRevolutions = BitConverter.ToUInt32(data, offset);
                ushort lastWheelEventTime = BitConverter.ToUInt16(data, offset + 4);
                // Speed calculation would need wheel circumference
                offset += 6;
            }

            // Crank Revolution Data (if present) - can calculate cadence from this
            if (crankRevolutionDataPresent && data.Length >= offset + 4)
            {
                ushort cumulativeCrankRevolutions = BitConverter.ToUInt16(data, offset);
                ushort lastCrankEventTime = BitConverter.ToUInt16(data, offset + 2);
                // Cadence calculation: if we have time difference, calculate RPM
                // For now, we'll need to track previous values to calculate cadence
                offset += 4;
            }

            // Other optional fields would be parsed here...

            return new TrainerData(
                Connected: true,
                DeviceName: _device?.Name ?? "Tacx Trainer",
                PowerWatts: powerWatts,
                CadenceRpm: null,
                SpeedKph: null
            );
        }
    }

    public record TrainerData(bool Connected, string DeviceName, double PowerWatts, double? CadenceRpm, double? SpeedKph);
}

