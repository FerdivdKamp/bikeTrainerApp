using System;
using System.Threading;
using System.Threading.Tasks;

namespace ErgTrainer.Sensors
{
    /// <summary>
    /// Common abstraction for “something that gives heart rate”.
    /// </summary>
    public interface IHeartRateSensor
    {
        /// <summary>Human-readable name, e.g. "ANT+ HRM" or "BLE HRM".</summary>
        string Name { get; }

        /// <summary>
        /// Connect and start producing heart-rate values.
        /// Returns true if a sensor was found and connected.
        /// </summary>
        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Stop receiving data and release resources.
        /// </summary>
        Task DisconnectAsync();

        /// <summary>
        /// Fired whenever a new HR value arrives (bpm).
        /// </summary>
        event EventHandler<int>? HeartRateReceived;
    }
}
