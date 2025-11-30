using SmallEarthTech.AntPlus;
using SmallEarthTech.AntPlus.DeviceProfiles;
using Microsoft.Extensions.Logging;

using SmallEarthTech.AntUsbStick;   // 👈 needed for AntUsb
using SmallEarthTech.AntRadioInterface;   // for IAntRadio

using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
// If you put IHeartRateSensor in a namespace like ErgTrainer.Sensors,
// make sure this matches:
using ErgTrainer.Sensors;


namespace ErgTrainer.Sensors
{
    public sealed class AntPlusHeartRateSensor : IHeartRateSensor
    {
        private readonly AntDeviceCollection _devices;
        private HeartRate? _hrm;

        public string Name => "ANT+ HRM";

        /// <summary>
        /// Raised whenever a new heart-rate value is available.
        /// </summary>
        public event EventHandler<int>? HeartRateReceived;

        // 👇 NEW: convenience ctor – creates USB radio + device collection for you
        public AntPlusHeartRateSensor()
            : this(CreateDeviceCollection())
        {
            // nothing else needed here – chained to main ctor
        }

        // Existing ctor: if you already have an AntDeviceCollection, you can still pass it in
        public AntPlusHeartRateSensor(AntDeviceCollection devices)
        {
            _devices = devices;

            Debug.WriteLine("[ANT] AntPlusHeartRateSensor created");

            _devices.CollectionChanged += Devices_CollectionChanged;

            // In case devices already exist when we start
            TryAttachHeartRate();
        }

        // Update the CreateDeviceCollection method to provide a logger to AntRadio
        private static AntDeviceCollection CreateDeviceCollection()
        {
            // Create a logger factory (writes to Debug output window)
            ILoggerFactory loggerFactory = LoggerFactory.Create(
                builder => builder.AddDebug());

            ILogger<AntRadio> logger = loggerFactory.CreateLogger<AntRadio>();

            // Create the ANT radio, passing the logger factory
            IAntRadio radio = new AntRadio(logger);

            // Create the device collection, also passing the logger factory
            var devices = new AntDeviceCollection(radio, loggerFactory);

            return devices;
        }



        public Task<bool> ConnectAsync(CancellationToken ct = default)
        {
            Debug.WriteLine("[ANT] ConnectAsync called");

            // AntDeviceCollection constructor already put radio in scan mode.
            bool attached = TryAttachHeartRate();
            return Task.FromResult(attached);
        }

        public Task DisconnectAsync()
        {
            Debug.WriteLine("[ANT] DisconnectAsync called");

            if (_hrm != null)
            {
                _hrm.PropertyChanged -= Hrm_PropertyChanged;
                _hrm = null;
            }

            return Task.CompletedTask;
        }

        private void Devices_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            Debug.WriteLine($"[ANT] CollectionChanged: action={e.Action}, total devices={_devices.Count}");

            foreach (var d in _devices)
            {
                Debug.WriteLine($"[ANT]  device: {d.GetType().FullName}  ToString()={d}");
            }

            if (_hrm == null)
            {
                TryAttachHeartRate();
            }
        }

        private bool TryAttachHeartRate()
        {
            Debug.WriteLine($"[ANT] TryAttachHeartRate: current device count = {_devices.Count}");

            var hr = _devices.OfType<HeartRate>().FirstOrDefault();
            if (hr == null)
            {
                Debug.WriteLine("[ANT]   no HeartRate device found yet");
                return false;
            }

            if (ReferenceEquals(hr, _hrm))
            {
                Debug.WriteLine("[ANT]   HeartRate device already attached");
                return true;
            }

            if (_hrm != null)
            {
                _hrm.PropertyChanged -= Hrm_PropertyChanged;
            }

            _hrm = hr;
            _hrm.PropertyChanged += Hrm_PropertyChanged;

            Debug.WriteLine($"[ANT]   Attached to HeartRate device: {_hrm}");
            return true;
        }

        private void Hrm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (_hrm == null) return;

            if (e.PropertyName == nameof(HeartRate.HeartRateData))
            {
                var bpm = _hrm.HeartRateData.ComputedHeartRate;
                Debug.WriteLine($"[ANT] HeartRateData changed – BPM={bpm}");

                if (bpm > 0)
                {
                    HeartRateReceived?.Invoke(this, bpm);
                }
            }
        }
    }
}
