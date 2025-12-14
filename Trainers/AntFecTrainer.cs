using ErgTrainer.Trainers;
using Microsoft.Extensions.Logging;
using SmallEarthTech.AntPlus;
using SmallEarthTech.AntPlus.DeviceProfiles;
using SmallEarthTech.AntPlus.DeviceProfiles.FitnessEquipment;
using SmallEarthTech.AntRadioInterface;
using SmallEarthTech.AntUsbStick;
using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace ErgTrainer.Trainers
{
    public sealed class AntFecTrainer : ITrainer
    {
        private readonly AntDeviceCollection _devices;
        private FitnessEquipment? _fe;   // or FeController, depending on your library version

        public string Name => "ANT+ FE-C Trainer";

        public event EventHandler<int>? PowerUpdated;
        public event EventHandler<int>? CadenceUpdated;

        // Convenience ctor: create radio + device collection
        public AntFecTrainer()
            : this(CreateDeviceCollection())
        {
        }

        // Advanced ctor: reuse an existing AntDeviceCollection
        public AntFecTrainer(AntDeviceCollection devices)
        {
            _devices = devices;

            Debug.WriteLine("[ANT-FEC] AntFecTrainer created");

            _devices.CollectionChanged += Devices_CollectionChanged;
            TryAttachTrainer();
        }

        private static AntDeviceCollection CreateDeviceCollection()
        {
            ILoggerFactory loggerFactory = LoggerFactory.Create(
                builder => builder.AddDebug());
            ILogger<AntRadio> logger = loggerFactory.CreateLogger<AntRadio>();

            IAntRadio radio = new AntRadio(logger);
            var devices = new AntDeviceCollection(radio, loggerFactory);

            return devices;
        }

        public Task<bool> ConnectAsync(CancellationToken cancellationToken = default)
        {
            Debug.WriteLine("[ANT-FEC] ConnectAsync called");
            bool attached = TryAttachTrainer();
            return Task.FromResult(attached);
        }

        public Task DisconnectAsync()
        {
            Debug.WriteLine("[ANT-FEC] DisconnectAsync called");

            if (_fe != null)
            {
                _fe.PropertyChanged -= Fe_PropertyChanged;
                _fe = null;
            }

            return Task.CompletedTask;
        }

        private void Devices_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Debug.WriteLine($"[ANT-FEC] CollectionChanged: action={e.Action}, total devices={_devices.Count}");

            foreach (var d in _devices)
            {
                Debug.WriteLine($"[ANT-FEC]  device: {d.GetType().FullName}  ToString()={d}");
            }

            if (_fe == null)
                TryAttachTrainer();
        }

        private bool TryAttachTrainer()
        {
            Debug.WriteLine($"[ANT-FEC] TryAttachTrainer: current device count = {_devices.Count}");

            // Library type may be FitnessEquipment or FeController; adjust if needed
            _fe = _devices.OfType<FitnessEquipment>().FirstOrDefault();
            if (_fe == null)
            {
                Debug.WriteLine("[ANT-FEC]   no FE-C trainer found yet");
                return false;
            }

            _fe.PropertyChanged -= Fe_PropertyChanged;
            _fe.PropertyChanged += Fe_PropertyChanged;

            Debug.WriteLine($"[ANT-FEC]   Attached to trainer: {_fe}");
            return true;
        }

        private void Fe_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (_fe == null) return;

            // You may need to adjust property names depending on the library’s model
            try
            {
                if (e.PropertyName == nameof(FitnessEquipment.InstantaneousPower))
                {
                    int watts = _fe.InstantaneousPower;
                    if (watts > 0)
                        PowerUpdated?.Invoke(this, watts);
                }

                if (e.PropertyName == nameof(FitnessEquipment.Cadence))
                {
                    int rpm = _fe.Cadence;
                    if (rpm > 0)
                        CadenceUpdated?.Invoke(this, rpm);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ANT-FEC] Fe_PropertyChanged error: {ex}");
            }
        }

        public Task SetTargetPowerAsync(int watts)
        {
            if (_fe == null)
            {
                Debug.WriteLine("[ANT-FEC] SetTargetPowerAsync called but trainer not attached.");
                return Task.CompletedTask;
            }

            Debug.WriteLine($"[ANT-FEC] SetTargetPowerAsync: {watts} W");

            // The exact API name may be SetTargetPower or similar – adjust to match your version
            _fe.SetTargetPower((ushort)watts);

            return Task.CompletedTask;
        }

        public Task SetGradeAsync(double gradePercent)
        {
            if (_fe == null)
            {
                Debug.WriteLine("[ANT-FEC] SetGradeAsync called but trainer not attached.");
                return Task.CompletedTask;
            }

            Debug.WriteLine($"[ANT-FEC] SetGradeAsync: {gradePercent}%");

            // Many FE-C implementations use “track resistance” to encode grade.
            // You may need to tweak this call based on actual API:
            // _fe.SetTrackResistance(gradePercent / 100.0, rollingResistanceCoeff, windCoeff);
            // For now, just call a placeholder if available.
            // _fe.SetGrade(gradePercent); // if your version exposes such a method

            return Task.CompletedTask;
        }
    }
}
