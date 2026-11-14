using Android.Bluetooth;
using Android.Bluetooth.LE;
using InTheHand.Bluetooth;
using BluetoothDevice = InTheHand.Bluetooth.BluetoothDevice;

namespace ErgTrainer.AndroidApp;

// Android can report the advertised name separately from the cached device name.
internal static class DeviceScanner
{
    internal sealed record FoundDevice(BluetoothDevice Device, string Name, string Id);

    public static async Task<IReadOnlyList<FoundDevice>> ScanAsync(
        BluetoothManager manager, CancellationToken cancellationToken)
    {
        var adapter = manager.Adapter;
        if (adapter == null || !adapter.IsEnabled)
            throw new InvalidOperationException("Turn on Bluetooth and try again.");
        var scanner = adapter.BluetoothLeScanner
            ?? throw new InvalidOperationException("Bluetooth LE scanning is unavailable.");
        using var callback = new ScanResults();
        using var settings = new ScanSettings.Builder()!
            .SetScanMode(global::Android.Bluetooth.LE.ScanMode.LowLatency)!
            .Build() ?? throw new InvalidOperationException("Could not configure Bluetooth scanning.");
        scanner.StartScan(null, settings, callback);
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken);
            return callback.GetResults();
        }
        finally
        {
            scanner.StopScan(callback);
        }
    }

    private sealed class ScanResults : ScanCallback
    {
        private readonly object gate = new();
        private readonly Dictionary<string, FoundDevice> found = new();
        private string? error;

        public override void OnScanResult(ScanCallbackType callbackType, ScanResult? result)
        {
            if (result?.Device == null) return;
            BluetoothAdvertisingEvent advertisement = result;
            var device = advertisement.Device;
            if (device == null || string.IsNullOrEmpty(device.Id)) return;
            string? advertisedName = result.ScanRecord?.DeviceName;
            string? cachedName = device.Name;
            lock (gate)
            {
                found.TryGetValue(device.Id, out var previous);
                string name = !string.IsNullOrWhiteSpace(advertisedName) ? advertisedName :
                    !string.IsNullOrWhiteSpace(cachedName) ? cachedName :
                    previous?.Name ?? "";
                found[device.Id] = new FoundDevice(device, name, device.Id);
            }
        }

        public override void OnScanFailed(ScanFailure errorCode)
        {
            lock (gate) error = $"Bluetooth scan failed ({errorCode}). Try again.";
        }

        public IReadOnlyList<FoundDevice> GetResults()
        {
            lock (gate)
            {
                if (error != null) throw new InvalidOperationException(error);
                return found.Values.OrderBy(d => string.IsNullOrWhiteSpace(d.Name))
                    .ThenBy(d => d.Name, StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }
    }
}
