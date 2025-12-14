using System;
using System.Threading;
using System.Threading.Tasks;

namespace ErgTrainer.Trainers
{
    public interface ITrainer
    {
        string Name { get; }

        Task<bool> ConnectAsync(CancellationToken cancellationToken = default);
        Task DisconnectAsync();

        /// <summary>Raised when new power data (watts) is available.</summary>
        event EventHandler<int>? PowerUpdated;

        /// <summary>Raised when new cadence data (rpm) is available.</summary>
        event EventHandler<int>? CadenceUpdated;

        /// <summary>Set target power in watts (ERG mode).</summary>
        Task SetTargetPowerAsync(int watts);

        /// <summary>Set road grade in percent (e.g. 5.0 = 5% climb).</summary>
        Task SetGradeAsync(double gradePercent);
    }
}
