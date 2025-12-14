using InTheHand.Bluetooth;
using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ErgTrainer.Sensors
{
    /// <summary>
    /// BLE Tacx trainer (Neo or similar). Prefers FTMS Indoor Bike Data.
    /// Tacx Neo often appends cadence/power even when FTMS flags say they're absent.
    /// </summary>
    public sealed class BleTacxTrainer
    {
        // Services
        private static readonly Guid FitnessMachineServiceUuid =
            Guid.Parse("00001826-0000-1000-8000-00805f9b34fb");

        private static readonly Guid CyclingPowerServiceUuid =
            Guid.Parse("00001818-0000-1000-8000-00805f9b34fb");

        // Characteristics
        private static readonly Guid IndoorBikeDataUuid =
            Guid.Parse("00002ad2-0000-1000-8000-00805f9b34fb");

        private static readonly Guid CyclingPowerMeasurementUuid =
            Guid.Parse("00002a63-0000-1000-8000-00805f9b34fb");

        public string Name => "BLE Tacx Trainer";
        public event EventHandler<TrainerData>? DataUpdated;

        private GattCharacteristic? _ftmsChar; // 0x2AD2
        private GattCharacteristic? _cpsChar;  // 0x2A63

        private BluetoothDevice? _device;
        private GattService? _service;
        private GattCharacteristic? _dataChar;
        private CancellationTokenSource? _pollingCts;

        // last-known values to suppress bogus spikes
        private double _lastSpeedKph;
        private double _lastCadenceRpm;
        private double _lastPowerWatts;

        private double _latestSpeedKph;
        private double _latestPowerWatts;
        private double _latestCadenceRpm;

        // ---------- Public API ----------

        public static async Task<bool> IsTacxTrainerAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
        {
            if (device == null) return false;

            Debug.WriteLine($"[Tacx] Checking device Name='{device.Name}', Id='{device.Id}'");

            try
            {
                await TryConnectAsync(device).ConfigureAwait(false);

                var gatt = device.Gatt;

                // Check for FTMS first
                var ftms = await gatt.GetPrimaryServiceAsync(FitnessMachineServiceUuid).ConfigureAwait(false);
                if (ftms != null) return true;

                // Fallback: Cycling Power service also common
                var cps = await gatt.GetPrimaryServiceAsync(CyclingPowerServiceUuid).ConfigureAwait(false);
                return cps != null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Tacx] IsTacxTrainerAsync failed: {ex}");
                try { device.Gatt.Disconnect(); } catch { }
                return false;
            }
        }

        public async Task<bool> ConnectToDeviceAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
        {
            if (device == null) return false;

            _device = device;
            await TryConnectAsync(_device).ConfigureAwait(false);

            var gatt = _device.Gatt;
            Debug.WriteLine($"[Tacx] Connecting to Name='{_device.Name}', Id='{_device.Id}'");

            // Discover both services (independent)
            var ftmsService = await gatt.GetPrimaryServiceAsync(FitnessMachineServiceUuid).ConfigureAwait(false);
            var cpsService = await gatt.GetPrimaryServiceAsync(CyclingPowerServiceUuid).ConfigureAwait(false);

            if (ftmsService == null && cpsService == null)
            {
                Debug.WriteLine("[Tacx] Neither FTMS nor Cycling Power Service found.");
                return false;
            }

            // Get characteristics (independent)
            if (ftmsService != null)
            {
                Debug.WriteLine("[Tacx] FTMS service found.");
                _ftmsChar = await ftmsService.GetCharacteristicAsync(IndoorBikeDataUuid).ConfigureAwait(false);
                if (_ftmsChar == null) Debug.WriteLine("[Tacx] Indoor Bike Data (2AD2) not found.");
            }

            if (cpsService != null)
            {
                Debug.WriteLine("[Tacx] Cycling Power service found.");
                _cpsChar = await cpsService.GetCharacteristicAsync(CyclingPowerMeasurementUuid).ConfigureAwait(false);
                if (_cpsChar == null) Debug.WriteLine("[Tacx] Cycling Power Measurement (2A63) not found.");
            }

            // If we got neither characteristic, we can't proceed
            if (_ftmsChar == null && _cpsChar == null)
            {
                Debug.WriteLine("[Tacx] No usable characteristics found (no 2AD2 and no 2A63).");
                return false;
            }

            // Subscribe notifications on whichever we have
            await StartNotificationsAsync(_ftmsChar, "[Tacx][FTMS]").ConfigureAwait(false);
            await StartNotificationsAsync(_cpsChar, "[Tacx][CPS]").ConfigureAwait(false);

            // Poll loop (backup) - poll both if available
            _pollingCts = new CancellationTokenSource();
            _ = Task.Run(() => PollLoopAsync(_pollingCts.Token));

            return true;
        }

        private async Task StartNotificationsAsync(GattCharacteristic? ch, string tag)
        {
            if (ch == null) return;

            try
            {
                ch.CharacteristicValueChanged += OnCharacteristicValueChanged;
                await ch.StartNotificationsAsync().ConfigureAwait(false);
                Debug.WriteLine($"{tag} Notifications started.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{tag} StartNotificationsAsync FAILED: {ex}");
            }
        }



        public async Task DisconnectAsync()
        {
            _pollingCts?.Cancel();
            _pollingCts = null;

            await StopCharAsync(_ftmsChar, "[Tacx][FTMS]").ConfigureAwait(false);
            await StopCharAsync(_cpsChar, "[Tacx][CPS]").ConfigureAwait(false);

            _ftmsChar = null;
            _cpsChar = null;

            try { _device?.Gatt?.Disconnect(); } catch { }
            _device = null;
        }

        private static async Task StopCharAsync(GattCharacteristic? ch, string tag)
        {
            if (ch == null) return;

            try
            {
                ch.CharacteristicValueChanged -= null; // you can't remove null; see note below
                await ch.StopNotificationsAsync().ConfigureAwait(false);
                Debug.WriteLine($"{tag} Notifications stopped.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"{tag} StopNotificationsAsync failed: {ex.Message}");
            }
        }


        // ---------- Event / polling ----------

        private void OnCharacteristicValueChanged(object? sender, GattCharacteristicValueChangedEventArgs e)
        {
            if (sender is not GattCharacteristic ch) return;
            var data = e.Value;
            if (data == null || data.Length < 2) return;

            HandleIncoming(ch, data);
        }

        private void HandleIncoming(GattCharacteristic ch, byte[] data)
        {
            // Always log at least something while debugging
            Debug.WriteLine($"[Tacx] RX ({(ReferenceEquals(ch, _ftmsChar) ? "FTMS" : ReferenceEquals(ch, _cpsChar) ? "CPS" : "??")}): {data.Length} bytes: {BitConverter.ToString(data)}");

            // FTMS -> update speed (and possibly others if present)
            if (_ftmsChar != null && ReferenceEquals(ch, _ftmsChar))
            {
                if (TryParseTacxNeoFtmsFixed(data, out var s) || TryParseGenericFtmsIndoorBikeData(data, out s))
                {
                    s = ApplySanityAndHold(s);
                    _latestSpeedKph = s.SpeedKph;

                    // If FTMS ever includes these (sometimes it does), keep them too:
                    if (s.PowerWatts > 0) _latestPowerWatts = s.PowerWatts;
                    if (s.CadenceRpm > 0) _latestCadenceRpm = s.CadenceRpm;

                    Debug.WriteLine($"[Tacx] FTMS parsed -> Speed={_latestSpeedKph:F1}kph");
                }
                else
                {
                    Debug.WriteLine("[Tacx] FTMS parse failed");
                }
            }

            // CPS -> update power (cadence is not derived yet in your CPS parser)
            if (_cpsChar != null && ReferenceEquals(ch, _cpsChar))
            {
                if (TryParseCyclingPowerMeasurement(data, out var s))
                {
                    s = ApplySanityAndHold(s);
                    _latestPowerWatts = s.PowerWatts;

                    // cadence stays 0 until you implement crank-event cadence in CPS
                    Debug.WriteLine($"[Tacx] CPS parsed -> Power={_latestPowerWatts:F0}W");
                }
                else
                {
                    Debug.WriteLine("[Tacx] CPS parse failed");
                }
            }

            EmitCombinedTrainerData();
        }


        private void EmitCombinedTrainerData()
        {
            DataUpdated?.Invoke(this, new TrainerData(
                Connected: true,
                DeviceName: _device?.Name ?? "Tacx Trainer",
                PowerWatts: _latestPowerWatts,
                CadenceRpm: _latestCadenceRpm,
                SpeedKph: _latestSpeedKph
            ));
        }


        private async Task PollLoopAsync(CancellationToken token)
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    if (_ftmsChar != null)
                    {
                        var data = await _ftmsChar.ReadValueAsync().ConfigureAwait(false);
                        if (data != null && data.Length >= 2)
                            HandleIncoming(_ftmsChar, data);
                    }

                    if (_cpsChar != null)
                    {
                        var data = await _cpsChar.ReadValueAsync().ConfigureAwait(false);
                        if (data != null && data.Length >= 2)
                            HandleIncoming(_cpsChar, data);
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    Debug.WriteLine($"[Tacx] Poll error: {ex.Message}");
                }

                try { await Task.Delay(1000, token).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
        }


        // ---------- Parsing ----------

        private bool TryParseTrainerData(byte[] data, out TrainerData trainerData)
        {
            trainerData = default;

            // Heuristic: FTMS Indoor Bike Data has 1-byte flags (small) and often starts with 0x0?
            // Cycling Power Measurement has 2-byte flags and power follows.
            bool looksFtms = data[0] < 0x20;

            TrainerSample sample;

            if (looksFtms)
            {
                // 1) Tacx Neo common pattern: flags 0x03 and length 14
                //    Tacx frequently appends cadence/power even if flags don't indicate them.
                if (TryParseTacxNeoFtmsFixed(data, out sample) ||
                    TryParseGenericFtmsIndoorBikeData(data, out sample))
                {
                    sample = ApplySanityAndHold(sample);
                    trainerData = ToTrainerData(sample);
                    Debug.WriteLine($"[Tacx] Parsed FTMS: Speed={trainerData.SpeedKph:F1}kph Cad={trainerData.CadenceRpm:F0}rpm Power={trainerData.PowerWatts:F0}W");
                    return true;
                }

                // If it “looked FTMS” but didn’t parse, try Cycling Power as fallback
                if (TryParseCyclingPowerMeasurement(data, out sample))
                {
                    sample = ApplySanityAndHold(sample);
                    trainerData = ToTrainerData(sample);
                    Debug.WriteLine($"[Tacx] Parsed CPS (fallback): Power={trainerData.PowerWatts:F0}W");
                    return true;
                }

                return false;
            }
            else
            {
                // 2) Cycling Power first
                if (TryParseCyclingPowerMeasurement(data, out sample))
                {
                    sample = ApplySanityAndHold(sample);
                    trainerData = ToTrainerData(sample);
                    Debug.WriteLine($"[Tacx] Parsed CPS: Power={trainerData.PowerWatts:F0}W");
                    return true;
                }

                // 3) FTMS as last resort
                if (TryParseGenericFtmsIndoorBikeData(data, out sample))
                {
                    sample = ApplySanityAndHold(sample);
                    trainerData = ToTrainerData(sample);
                    Debug.WriteLine($"[Tacx] Parsed FTMS (fallback): Speed={trainerData.SpeedKph:F1}kph");
                    return true;
                }

                return false;
            }
        }

        /// <summary>
        /// Tacx Neo “common” FTMS packet we see in practice:
        /// flags=0x03 (speed + avg speed) but still 14 bytes and contains cadence/power in fixed offsets.
        /// </summary>
        private static bool TryParseTacxNeoFtmsFixed(byte[] data, out TrainerSample sample)
        {
            sample = default;

            // Your common packet shape: 14 bytes and flags = 0x0003 (shown as "03-00")
            if (data.Length != 14) return false;

            ushort flags = BitConverter.ToUInt16(data, 0);
            if (flags != 0x0003) return false;

            // After 2-byte flags:
            // 2-3: instantaneous speed (0.01 km/h)
            // 4-5: average speed (0.01 km/h)
            ushort speedRaw = BitConverter.ToUInt16(data, 2);
            double speedKph = speedRaw * 0.01;

            // Do NOT guess cadence/power from the remaining bytes in this packet shape.
            // Use Cycling Power Measurement characteristic for power/cadence instead.
            sample = new TrainerSample(
                SpeedKph: speedKph,
                CadenceRpm: 0,
                PowerWatts: 0
            );

            return true;
        }


        /// <summary>
        /// Spec-compliant FTMS Indoor Bike Data parser (Bluetooth SIG order).
        /// Only reads fields when flags say present.
        /// </summary>
        private static bool TryParseGenericFtmsIndoorBikeData(byte[] data, out TrainerSample sample)
        {
            sample = default;
            if (data.Length < 4) return false; // need at least flags(2) + speed(2) in practice

            // FTMS Indoor Bike Data: Flags is UINT16 (2 bytes), little-endian
            ushort flags = BitConverter.ToUInt16(data, 0);

            bool moreData = (flags & 0x0001) != 0; // Instantaneous Speed present
            bool avgSpeedPresent = (flags & 0x0002) != 0; // Average Speed present
            bool instCadPresent = (flags & 0x0004) != 0; // Instantaneous Cadence present
            bool avgCadPresent = (flags & 0x0008) != 0; // Average Cadence present
            bool totalDistancePresent = (flags & 0x0010) != 0; // Total Distance present (3 bytes)
            bool resistanceLevelPresent = (flags & 0x0020) != 0; // Resistance Level present
            bool instPowerPresent = (flags & 0x0040) != 0; // Instantaneous Power present
            bool avgPowerPresent = (flags & 0x0080) != 0; // Average Power present

            int offset = 2; // <-- start after 2-byte flags

            double speedKph = 0;
            double cadenceRpm = 0;
            double powerWatts = 0;

            // 1) Instantaneous Speed (0.01 km/h)
            if (moreData && data.Length >= offset + 2)
            {
                speedKph = BitConverter.ToUInt16(data, offset) * 0.01;
                offset += 2;
            }

            // 2) Average Speed (0.01 km/h)
            if (avgSpeedPresent && data.Length >= offset + 2)
            {
                // you can parse it if you want; for now just consume
                offset += 2;
            }

            // 3) Instantaneous Cadence (0.5 rpm)
            if (instCadPresent && data.Length >= offset + 2)
            {
                cadenceRpm = BitConverter.ToUInt16(data, offset) * 0.5;
                offset += 2;
            }

            // 4) Average Cadence
            if (avgCadPresent && data.Length >= offset + 2) offset += 2;

            // 5) Total Distance (3 bytes)
            if (totalDistancePresent && data.Length >= offset + 3) offset += 3;

            // 6) Resistance Level
            if (resistanceLevelPresent && data.Length >= offset + 2) offset += 2;

            // 7) Instantaneous Power (signed int16 watts)
            if (instPowerPresent && data.Length >= offset + 2)
            {
                powerWatts = BitConverter.ToInt16(data, offset);
                offset += 2;
            }

            // 8) Average Power
            if (avgPowerPresent && data.Length >= offset + 2) offset += 2;

            if (speedKph == 0 && cadenceRpm == 0 && powerWatts == 0)
                return false;

            sample = new TrainerSample(speedKph, cadenceRpm, powerWatts);
            return true;
        }


        private static bool TryParseCyclingPowerMeasurement(byte[] data, out TrainerSample sample)
        {
            sample = default;
            if (data.Length < 4) return false;

            // Flags (2 bytes) + Instantaneous Power (2 bytes, signed) always present
            // https://www.bluetooth.com/specifications/specs/cycling-power-service-1-1/
            // We are not deriving cadence/speed here; that would require tracking crank/wheel events.
            short power = BitConverter.ToInt16(data, 2);

            // Reject clearly impossible “power” early (avoid mis-detecting random data)
            if (power < -50 || power > 3000) return false;

            sample = new TrainerSample(SpeedKph: 0, CadenceRpm: 0, PowerWatts: power);
            return true;
        }

        // ---------- Sanity / smoothing ----------

        private TrainerSample ApplySanityAndHold(TrainerSample s)
        {
            // Speed: Tacx / apps will never show 150-500 km/h
            if (s.SpeedKph < 0 || s.SpeedKph > 80) s = s with { SpeedKph = _lastSpeedKph };
            else _lastSpeedKph = s.SpeedKph;

            // Cadence: ignore zeros and silly spikes; keep last if out of range.
            // If you want to allow trackstands, adjust lower bound.
            if (s.CadenceRpm < 20 || s.CadenceRpm > 140) s = s with { CadenceRpm = _lastCadenceRpm };
            else _lastCadenceRpm = s.CadenceRpm;

            // Power: negative can happen in some vendor modes; for now clamp to last-known if out of range.
            if (s.PowerWatts < 0 || s.PowerWatts > 2000) s = s with { PowerWatts = _lastPowerWatts };
            else _lastPowerWatts = s.PowerWatts;

            return s;
        }

        private TrainerData ToTrainerData(TrainerSample s) =>
            new(
                Connected: true,
                DeviceName: _device?.Name ?? "Tacx Trainer",
                PowerWatts: s.PowerWatts,
                CadenceRpm: s.CadenceRpm,
                SpeedKph: s.SpeedKph
            );

        private static async Task TryConnectAsync(BluetoothDevice device)
        {
            try
            {
                await device.Gatt.ConnectAsync().ConfigureAwait(false);
            }
            catch
            {
                // Some stacks throw if already connected – safe to ignore
            }
        }




        private readonly record struct TrainerSample(double SpeedKph, double CadenceRpm, double PowerWatts);
    }

    public record TrainerData(bool Connected, string DeviceName, double PowerWatts, double CadenceRpm, double SpeedKph);
}
