using InTheHand.Bluetooth;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace ErgTrainer.Sensors
{
    /// <summary>
    /// Handles scanning for Bluetooth devices. This is separate from device-specific functionality.
    /// </summary>
    public static class BluetoothDeviceScanner
    {
        /// <summary>
        /// Scans for available Bluetooth devices.
        /// </summary>
        public static async Task<IReadOnlyCollection<BluetoothDevice>> ScanForDevicesAsync(CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("[BLE Scanner] ScanForDevicesAsync: starting");

            bool available;
            try
            {
                Debug.WriteLine("[BLE Scanner] Checking availability…");
                available = await Bluetooth.GetAvailabilityAsync();
                Debug.WriteLine($"[BLE Scanner] Availability = {available}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BLE Scanner] GetAvailabilityAsync failed: {ex}");
                return Array.Empty<BluetoothDevice>();
            }

            if (!available)
            {
                Debug.WriteLine("[BLE Scanner] Bluetooth not available – aborting.");
                return Array.Empty<BluetoothDevice>();
            }

            Debug.WriteLine("[BLE Scanner] Scanning for devices…");
            IReadOnlyCollection<BluetoothDevice> devices;
            try
            {
                devices = await Bluetooth.ScanForDevicesAsync();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[BLE Scanner] ScanForDevicesAsync failed: {ex}");
                return Array.Empty<BluetoothDevice>();
            }

            Debug.WriteLine($"[BLE Scanner] Scan finished. Found {devices.Count} device(s).");
            foreach (var d in devices)
            {
                Debug.WriteLine($"[BLE Scanner]   Device: Name='{d.Name}', Id='{d.Id}'");
            }

            return devices;
        }
    }
}

