using System;
using System.Globalization;
using System.IO;
using ErgTrainer.Sensors;

namespace ErgTrainer.Recorders
{
    /// <summary>
    /// Stores raw BLE characteristic values in a CSV file for offline analysis.
    /// </summary>
    public sealed class SignalRecorder : IDisposable
    {
        private readonly object _sync = new();
        private StreamWriter? _writer;

        public bool IsRecording
        {
            get
            {
                lock (_sync)
                {
                    return _writer is not null;
                }
            }
        }

        public string? CurrentFilePath { get; private set; }

        public string StartRecording()
        {
            lock (_sync)
            {
                if (_writer is not null)
                {
                    return CurrentFilePath!;
                }

                var recordingsDirectory = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    "ErgTrainer",
                    "Recordings");
                Directory.CreateDirectory(recordingsDirectory);

                CurrentFilePath = Path.Combine(
                    recordingsDirectory,
                    $"raw-signals-{DateTime.Now:yyyyMMdd-HHmmssfff}.csv");

                _writer = new StreamWriter(CurrentFilePath)
                {
                    AutoFlush = true
                };
                _writer.WriteLine("timestamp_utc,source,device,delivery_method,service_uuid,characteristic_uuid,raw_hex");

                return CurrentFilePath;
            }
        }

        public void Record(RawDeviceData data)
        {
            lock (_sync)
            {
                if (_writer is null)
                {
                    return;
                }

                _writer.WriteLine(string.Join(",",
                    Escape(data.TimestampUtc.ToString("O", CultureInfo.InvariantCulture)),
                    Escape(data.Source),
                    Escape(data.DeviceName),
                    Escape(data.DeliveryMethod),
                    data.ServiceUuid,
                    data.CharacteristicUuid,
                    Convert.ToHexString(data.Payload)));
            }
        }

        public void StopRecording()
        {
            lock (_sync)
            {
                _writer?.Dispose();
                _writer = null;
            }
        }

        public void Dispose() => StopRecording();

        private static string Escape(string value) =>
            $"\"{value.Replace("\"", "\"\"")}\"";
    }
}
