using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using SmallEarthTech.AntPlus;
using SmallEarthTech.AntPlus.DeviceProfiles;

public sealed class AntPlusHeartRate : IHeartRateSensor
{
    private readonly AntDeviceCollection _devices;
    private HeartRate? _hrm;

    public event EventHandler<int>? HeartRateUpdated;

    public AntPlusHeartRate(AntDeviceCollection devices)
    {
        _devices = devices;

        // When new devices are discovered, this event fires.
        _devices.CollectionChanged += Devices_CollectionChanged;

        // Try immediately, in case the strap is already seen.
        TryAttachHeartRate();
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        // Creating AntDeviceCollection already started scan mode.
        // Just make sure we’re attached to a HRM if one exists.
        TryAttachHeartRate();
        return Task.CompletedTask;
    }

    public Task DisconnectAsync()
    {
        if (_hrm != null)
        {
            _hrm.PropertyChanged -= Hrm_PropertyChanged;
            _hrm = null;
        }
        return Task.CompletedTask;
    }

    // ---- internal plumbing ----

    private void Devices_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_hrm != null)
            return; // already attached

        TryAttachHeartRate();
    }

    private void TryAttachHeartRate()
    {
        // Find the first HeartRate device, if any
        var hr = _devices.OfType<HeartRate>().FirstOrDefault();
        if (hr == null || ReferenceEquals(hr, _hrm))
            return;

        if (_hrm != null)
            _hrm.PropertyChanged -= Hrm_PropertyChanged;

        _hrm = hr;
        _hrm.PropertyChanged += Hrm_PropertyChanged;
    }

    private void Hrm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (_hrm == null)
            return;

        // HeartRateData is a struct with ComputedHeartRate BPM.
        if (e.PropertyName == nameof(HeartRate.HeartRateData))
        {
            var bpm = _hrm.HeartRateData.ComputedHeartRate;
            if (bpm > 0)
            {
                HeartRateUpdated?.Invoke(this, bpm);
            }
        }
    }
}
