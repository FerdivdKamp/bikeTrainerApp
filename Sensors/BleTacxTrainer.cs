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

            // Detect format based on data structure:
            // FTMS: single byte flag (0x03, 0x07, etc. - typically < 0x10 for first byte)
            // Cycling Power: 2-byte flags (typically 0x0000-0xFFFF, but often starts with 0x00XX or 0xXX00)
            bool looksLikeFtms = data[0] < 0x20; // FTMS flags are typically small values
            
            TrainerData? ftmsResult = null;
            TrainerData? cyclingPowerResult = null;
            
            if (looksLikeFtms)
            {
                // Try FTMS first (most common for Tacx Neo)
                Debug.WriteLine("[Tacx] Data looks like FTMS format, parsing as FTMS");
                ftmsResult = ParseFtmsData(data);
                if (ftmsResult != null && (ftmsResult.SpeedKph > 0 || ftmsResult.PowerWatts > 0 || ftmsResult.CadenceRpm > 0))
                {
                    Debug.WriteLine($"[Tacx] Using FTMS result: Speed={ftmsResult.SpeedKph:F2}kph, Cadence={ftmsResult.CadenceRpm:F1}rpm, Power={ftmsResult.PowerWatts:F0}W");
                    return ftmsResult;
                }
            }
            
            // Fall back to Cycling Power format
            Debug.WriteLine("[Tacx] Trying Cycling Power Service format");
            cyclingPowerResult = ParseCyclingPowerData(data);
            if (cyclingPowerResult != null && (cyclingPowerResult.SpeedKph > 0 || cyclingPowerResult.PowerWatts > 0 || cyclingPowerResult.CadenceRpm > 0))
            {
                Debug.WriteLine($"[Tacx] Using Cycling Power result: Speed={cyclingPowerResult.SpeedKph:F2}kph, Cadence={cyclingPowerResult.CadenceRpm:F1}rpm, Power={cyclingPowerResult.PowerWatts:F0}W");
                return cyclingPowerResult;
            }
            
            // If FTMS wasn't tried yet, try it now
            if (!looksLikeFtms)
            {
                Debug.WriteLine("[Tacx] Also trying FTMS format");
                ftmsResult = ParseFtmsData(data);
                if (ftmsResult != null && (ftmsResult.SpeedKph > 0 || ftmsResult.PowerWatts > 0 || ftmsResult.CadenceRpm > 0))
                {
                    Debug.WriteLine($"[Tacx] Using FTMS result: Speed={ftmsResult.SpeedKph:F2}kph, Cadence={ftmsResult.CadenceRpm:F1}rpm, Power={ftmsResult.PowerWatts:F0}W");
                    return ftmsResult;
                }
            }

            // Return whichever gave us at least some data
            return ftmsResult ?? cyclingPowerResult;
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
                speedKph = speed * 0.01; // Speed in km/h with resolution 0.01
                Debug.WriteLine($"[Tacx] Instantaneous Speed @{offset}: {speed} -> {speedKph:F2} kph");
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
                // Common positions: offset 5-6, 7-8, 9-10
                if (cadenceRpm == 0)
                {
                    for (int cadOffset = offset; cadOffset <= offset + 6 && cadOffset + 2 <= data.Length; cadOffset += 2)
                    {
                        ushort cadence = BitConverter.ToUInt16(data, cadOffset);
                        double testCadence = cadence * 0.5;
                        
                        // Accept reasonable cadence values (5-200 rpm)
                        if (testCadence >= 5 && testCadence < 200)
                        {
                            cadenceRpm = testCadence;
                            Debug.WriteLine($"[Tacx] Cadence found @{cadOffset}: {cadence} -> {cadenceRpm:F1} rpm");
                            break;
                        }
                        else
                        {
                            Debug.WriteLine($"[Tacx] Cadence @{cadOffset}: {cadence} (*0.5 = {testCadence:F1} rpm, rejected)");
                        }
                    }
                }
                
                // Try power at various positions
                // Common positions: offset 9-10, 11-12
                if (powerWatts == 0)
                {
                    for (int powOffset = offset + 4; powOffset <= offset + 8 && powOffset + 2 <= data.Length; powOffset += 2)
                    {
                        ushort powerRaw = BitConverter.ToUInt16(data, powOffset);
                        short powerSigned = BitConverter.ToInt16(data, powOffset);
                        
                        // Try signed first (FTMS uses signed for power)
                        if (powerSigned >= 5 && powerSigned < 2000)
                        {
                            powerWatts = powerSigned;
                            Debug.WriteLine($"[Tacx] Power found @{powOffset} (signed): {powerSigned} -> {powerWatts:F0} W");
                            break;
                        }
                        // Try unsigned
                        else if (powerRaw >= 5 && powerRaw < 2000)
                        {
                            powerWatts = powerRaw;
                            Debug.WriteLine($"[Tacx] Power found @{powOffset} (unsigned): {powerRaw} -> {powerWatts:F0} W");
                            break;
                        }
                        else
                        {
                            Debug.WriteLine($"[Tacx] Power @{powOffset}: {powerRaw}/{powerSigned} (rejected)");
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

