using InTheHand.Bluetooth;
using System;
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
        
        // Cycling Power Measurement characteristic UUID
        private static readonly Guid CyclingPowerMeasurementUuid =
            Guid.Parse("00002a63-0000-1000-8000-00805f9b34fb");

        public string Name => "BLE Tacx Trainer";

        public event EventHandler<TrainerData>? DataUpdated;

        private BluetoothDevice? _device;
        private GattService? _ftmsService;
        private GattCharacteristic? _indoorBikeDataCharacteristic;
        private CancellationTokenSource? _pollingCts;

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
                // Disconnect only if it's not a trainer
                try
                {
                    gatt.Disconnect();
                }
                catch { }
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

            Debug.WriteLine("[Tacx] Starting polling loop (as backup)...");
            _pollingCts = new CancellationTokenSource();
            _ = Task.Run(() => PollLoopAsync(_pollingCts.Token), _pollingCts.Token);

            Debug.WriteLine("[Tacx] ConnectToDeviceAsync: success");
            return true;
        }

        public async Task DisconnectAsync()
        {
            // stop our polling loop
            _pollingCts?.Cancel();
            _pollingCts = null;

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

            // drop the device reference
            _device = null;
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
                }

                if (data != null && data.Length >= 2)
                {
                    // Parse FTMS Indoor Bike Data or Cycling Power Data
                    var trainerData = ParseIndoorBikeData(data);
                    if (trainerData != null)
                    {
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
                    }

                    if (data != null && data.Length >= 2)
                    {
                        // Parse FTMS Indoor Bike Data or Cycling Power Data
                        var trainerData = ParseIndoorBikeData(data);
                        if (trainerData != null)
                        {
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

        private TrainerData? ParseIndoorBikeData(byte[] data)
        {
            // Check if this is Cycling Power Service data or FTMS data
            // Cycling Power Measurement typically starts with flags, FTMS has different structure
            if (data.Length < 2) return null;

            // Try to detect which service we're using based on characteristic UUID
            // For now, try FTMS format first, then Cycling Power
            return ParseFtmsData(data) ?? ParseCyclingPowerData(data);
        }

        private TrainerData? ParseFtmsData(byte[] data)
        {
            // Simplified FTMS Indoor Bike Data parser
            // Full implementation would parse all flags and fields according to FTMS spec
            if (data.Length < 2) return null;

            // Flags byte
            byte flags = data[0];
            bool moreData = (flags & 0x01) != 0;
            bool averageSpeedPresent = (flags & 0x02) != 0;
            bool instantaneousCadencePresent = (flags & 0x04) != 0;
            bool averageCadencePresent = (flags & 0x08) != 0;
            bool totalDistancePresent = (flags & 0x10) != 0;
            bool resistanceLevelPresent = (flags & 0x20) != 0;
            bool instantaneousPowerPresent = (flags & 0x40) != 0;
            bool averagePowerPresent = (flags & 0x80) != 0;

            int offset = 1;
            double speedKph = 0;
            double cadenceRpm = 0;
            double powerWatts = 0;

            // Instantaneous Speed (if present)
            if (moreData && data.Length >= offset + 2)
            {
                ushort speed = BitConverter.ToUInt16(data, offset);
                speedKph = speed * 0.01; // Speed in km/h with resolution 0.01
                offset += 2;
            }

            // Average Speed (if present)
            if (averageSpeedPresent && data.Length >= offset + 2)
            {
                offset += 2;
            }

            // Instantaneous Cadence (if present)
            if (instantaneousCadencePresent && data.Length >= offset + 2)
            {
                ushort cadence = BitConverter.ToUInt16(data, offset);
                cadenceRpm = cadence * 0.5; // Cadence in rpm with resolution 0.5
                offset += 2;
            }

            // Average Cadence (if present)
            if (averageCadencePresent && data.Length >= offset + 2)
            {
                offset += 2;
            }

            // Total Distance (if present)
            if (totalDistancePresent && data.Length >= offset + 3)
            {
                offset += 3;
            }

            // Resistance Level (if present)
            if (resistanceLevelPresent && data.Length >= offset + 2)
            {
                offset += 2;
            }

            // Instantaneous Power (if present)
            if (instantaneousPowerPresent && data.Length >= offset + 2)
            {
                short power = BitConverter.ToInt16(data, offset);
                powerWatts = power; // Power in watts
                offset += 2;
            }

            // Average Power (if present)
            if (averagePowerPresent && data.Length >= offset + 2)
            {
                offset += 2;
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
            if (data.Length < 2) return null;

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
            double cadenceRpm = 0;
            double speedKph = 0;

            // Instantaneous Power (always present, 2 bytes, signed)
            if (data.Length >= offset + 2)
            {
                short power = BitConverter.ToInt16(data, offset);
                powerWatts = power;
                offset += 2;
            }

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
                CadenceRpm: cadenceRpm,
                SpeedKph: speedKph
            );
        }
    }

    public record TrainerData(bool Connected, string DeviceName, double PowerWatts, double CadenceRpm, double SpeedKph);
}

