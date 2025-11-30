using SmallEarthTech.AntPlus;
using SmallEarthTech.AntPlus.DeviceProfiles;
using System;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

public sealed class AntPlusHeartRate : IHeartRateSensor
{
    private readonly AntDeviceCollection _devices;
    private HeartRate? _hrm;

    public event EventHandler<int>? HeartRateUpdated;

    public AntPlusHeartRate(AntDeviceCollection devices)
    {
        _devices = devices;

        Debug.WriteLine("[ANT] AntPlusHeartRate created");

        _devices.CollectionChanged += Devices_CollectionChanged;

        // In case devices already exist when we start
        TryAttachHeartRate();
    }

    public Task ConnectAsync(CancellationToken ct = default)
    {
        Debug.WriteLine("[ANT] ConnectAsync called");

        // AntDeviceCollection constructor already put radio in scan mode.
        TryAttachHeartRate();
        return Task.CompletedTask;
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

    private void TryAttachHeartRate()
    {
        Debug.WriteLine($"[ANT] TryAttachHeartRate: current device count = {_devices.Count}");

        var hr = _devices.OfType<HeartRate>().FirstOrDefault();
        if (hr == null)
        {
            Debug.WriteLine("[ANT]   no HeartRate device found yet");
            return;
        }

        if (ReferenceEquals(hr, _hrm))
        {
            Debug.WriteLine("[ANT]   HeartRate device already attached");
            return;
        }

        if (_hrm != null)
        {
            _hrm.PropertyChanged -= Hrm_PropertyChanged;
        }

        _hrm = hr;
        _hrm.PropertyChanged += Hrm_PropertyChanged;

        Debug.WriteLine($"[ANT]   Attached to HeartRate device: {_hrm}");
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
                HeartRateUpdated?.Invoke(this, bpm);
            }
        }
    }
}
