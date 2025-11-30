using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ErgTrainer.Sensors;
using InTheHand.Bluetooth;

namespace ErgTrainer.Sensors
{
    /// <summary>
    /// BLE heart-rate monitor (Garmin HRM-Dual or similar).
    /// </summary>
    public class BleHeartRateSensor : IHeartRateSensor
    {
        // Standard Bluetooth UUIDs for the Heart Rate service & measurement characteristic.
        private static readonly Guid HeartRateServiceUuid =
            Guid.Parse("0000180d-0000-1000-8000-00805f9b34fb");
        private static readonly Guid HeartRateMeasurementUuid =
            Guid.Parse("00002a37-0000-1000-8000-00805f9b34fb");

        // Optional: tweak this to only match Garmin HRM strap names if you want
        private const string TargetNamePrefix = "HRM";

        public string Name => "BLE HRM";

        public event EventHandler<int>? HeartRateReceived;

        private BluetoothDevice? _device;
        private GattService? _hrService;
        private GattCharacteristic? _hrCharacteristic;
        private CancellationTokenSource? _pollingCts;

        public async Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            bool available;

            Debug.WriteLine("Trying to connect.");

            try
            {
                // 1) Check if Bluetooth is available
                available = await Bluetooth.GetAvailabilityAsync();
            }
            catch (Exception ex)
            {
                // Log and gracefully fail
                System.Diagnostics.Debug.WriteLine($"[BLE] GetAvailabilityAsync failed: {ex}");
                return false; // or set a flag and show "BLE not supported" in UI
            }

            if (!available)
                return false;

            // 2) Scan for devices
            var devices = await Bluetooth.ScanForDevicesAsync(); // may take ~30s default timeout

            // Try to pick a sensible device: first whose name starts with HRM, otherwise first with HR service
            _device = devices
                .FirstOrDefault(d => !string.IsNullOrEmpty(d.Name) &&
                                     d.Name.StartsWith(TargetNamePrefix, StringComparison.OrdinalIgnoreCase));

            if (_device == null)
            {
                // Fallback: just pick first device; user can refine this later
                _device = devices.FirstOrDefault();
            }

            if (_device == null)
                return false;

            // 3) Connect GATT
            var gatt = _device.Gatt;
            await gatt.ConnectAsync();

            // 4) Get heart-rate service + measurement characteristic
            _hrService = await gatt.GetPrimaryServiceAsync(HeartRateServiceUuid);
            if (_hrService == null)
                return false;

            _hrCharacteristic = await _hrService.GetCharacteristicAsync(HeartRateMeasurementUuid);
            if (_hrCharacteristic == null)
                return false;

            // 5) Start notifications if supported (non-blocking)
            try
            {
                await _hrCharacteristic.StartNotificationsAsync();
            }
            catch
            {
                // Not all platforms require/implement this; ignore if it fails
            }

            // 6) Start a background polling loop that reads HR once per second
            _pollingCts = new CancellationTokenSource();
            _ = Task.Run(() => PollLoopAsync(_pollingCts.Token), _pollingCts.Token);

            return true;
        }

        public async Task DisconnectAsync()
        {
            // stop our polling loop
            _pollingCts?.Cancel();
            _pollingCts = null;

            if (_hrCharacteristic != null)
            {
                try
                {
                    await _hrCharacteristic.StopNotificationsAsync();
                }
                catch
                {
                    // ignore errors on shutdown
                }

                _hrCharacteristic = null;
            }

            // we’re done with the service too
            _hrService = null;

            // drop the device reference – 32feet will clean up native objects
            _device = null;
        }

        private async Task PollLoopAsync(CancellationToken token)
        {
            if (_hrCharacteristic == null)
                return;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    byte[] data = await _hrCharacteristic.ReadValueAsync();

                    if (data.Length >= 2)
                    {
                        byte flags = data[0];
                        int hr;

                        // Bit 0 of flags = 0: HR is 8-bit in byte 1
                        // Bit 0 of flags = 1: HR is 16-bit in bytes 1–2 (little-endian)
                        if ((flags & 0x01) == 0)
                        {
                            hr = data[1];
                        }
                        else if (data.Length >= 3)
                        {
                            hr = data[1] | (data[2] << 8);
                        }
                        else
                        {
                            hr = 0;
                        }

                        if (hr > 0)
                        {
                            HeartRateReceived?.Invoke(this, hr);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                    // Swallow read errors; we’ll try again next tick
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
    }
}
