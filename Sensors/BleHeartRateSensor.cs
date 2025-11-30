using ErgTrainer.Sensors;
using InTheHand.Bluetooth;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

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
            Debug.WriteLine("[BLE] ConnectAsync: starting - scanning for devices...");

            var devices = await BluetoothDeviceScanner.ScanForDevicesAsync(cancellationToken);
            if (devices.Count == 0)
            {
                Debug.WriteLine("[BLE] No devices found.");
                return false;
            }

            _device = devices
                .FirstOrDefault(d => !string.IsNullOrEmpty(d.Name) &&
                                     d.Name.StartsWith(TargetNamePrefix, StringComparison.OrdinalIgnoreCase));

            if (_device == null)
            {
                Debug.WriteLine("[BLE] No device with HRM* name found, falling back to first device.");
                _device = devices.FirstOrDefault();
            }

            if (_device == null)
            {
                Debug.WriteLine("[BLE] Still no device – aborting.");
                return false;
            }

            return await ConnectToDeviceAsync(_device, cancellationToken);
        }

        /// <summary>
        /// Checks if a device is a heart rate monitor by attempting to connect and checking for the Heart Rate service.
        /// Note: Leaves the connection open for the actual ConnectToDeviceAsync to use.
        /// </summary>
        public static async Task<bool> IsHeartRateMonitorAsync(BluetoothDevice device, CancellationToken cancellationToken = default)
        {
            if (device == null)
            {
                Debug.WriteLine("[BLE] IsHeartRateMonitorAsync: device is null");
                return false;
            }

            Debug.WriteLine($"[BLE] IsHeartRateMonitorAsync: checking device Name='{device.Name}', Id='{device.Id}'");

            try
            {
                var gatt = device.Gatt;
                Debug.WriteLine("[BLE] Connecting GATT to check services…");
                await gatt.ConnectAsync();
                Debug.WriteLine("[BLE] GATT connected.");

                Debug.WriteLine("[BLE] Checking for HeartRate service…");
                var hrService = await gatt.GetPrimaryServiceAsync(HeartRateServiceUuid);

                if (hrService != null)
                {
                    Debug.WriteLine("[BLE] Device is a heart rate monitor.");
                    // Don't disconnect - leave connection open for ConnectToDeviceAsync to use
                    return true;
                }
                else
                {
                    Debug.WriteLine("[BLE] Device does not have HeartRate service.");
                    // Disconnect only if it's not a HRM
                    try
                    {
                        gatt.Disconnect();
                    }
                    catch { }
                    return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BLE] IsHeartRateMonitorAsync failed: {ex.Message}");
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
                Debug.WriteLine("[BLE] ConnectToDeviceAsync: device is null");
                return false;
            }

            Debug.WriteLine($"[BLE] ConnectToDeviceAsync: connecting to device Name='{device.Name}', Id='{device.Id}'");

            _device = device;

            var gatt = _device.Gatt;
            
            // Try to connect (might already be connected from IsHeartRateMonitorAsync)
            try
            {
                Debug.WriteLine("[BLE] Connecting GATT…");
                await gatt.ConnectAsync();
                Debug.WriteLine("[BLE] GATT connected.");
            }
            catch (ObjectDisposedException)
            {
                // GATT object was disposed, get a new one
                Debug.WriteLine("[BLE] GATT was disposed, getting new GATT instance…");
                gatt = _device.Gatt;
                await gatt.ConnectAsync();
                Debug.WriteLine("[BLE] GATT connected (new instance).");
            }
            catch (Exception ex)
            {
                // If already connected or other error, log and try to continue
                Debug.WriteLine($"[BLE] GATT connection attempt: {ex.Message} - continuing anyway");
            }

            Debug.WriteLine("[BLE] Getting HeartRate service…");
            _hrService = await gatt.GetPrimaryServiceAsync(HeartRateServiceUuid);
            if (_hrService == null)
            {
                Debug.WriteLine("[BLE] HeartRate service not found.");
                return false;
            }

            Debug.WriteLine("[BLE] Getting HeartRate measurement characteristic…");
            _hrCharacteristic = await _hrService.GetCharacteristicAsync(HeartRateMeasurementUuid);
            if (_hrCharacteristic == null)
            {
                Debug.WriteLine("[BLE] HeartRate characteristic not found.");
                return false;
            }

            Debug.WriteLine("[BLE] Starting notifications (if supported)...");
            try
            {
                await _hrCharacteristic.StartNotificationsAsync();
                Debug.WriteLine("[BLE] Notifications started.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BLE] StartNotificationsAsync failed (ignored): {ex.Message}");
            }

            Debug.WriteLine("[BLE] Starting polling loop…");
            _pollingCts = new CancellationTokenSource();
            _ = Task.Run(() => PollLoopAsync(_pollingCts.Token), _pollingCts.Token);

            Debug.WriteLine("[BLE] ConnectToDeviceAsync: success");
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
