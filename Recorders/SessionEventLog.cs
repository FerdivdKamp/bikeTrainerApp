using System;
using System.Diagnostics;
using System.IO;

namespace ErgTrainer.Recorders;

// Low-volume workout events; raw BLE packets remain in SignalRecorder.
public sealed class SessionEventLog : IDisposable
{
    private StreamWriter? _writer;
    public string? CurrentFilePath { get; private set; }

    public void Start()
    {
        Stop();
        try
        {
            string directory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                "ErgTrainer", "SessionLogs");
            Directory.CreateDirectory(directory);
            CurrentFilePath = Path.Combine(directory, $"session-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.csv");
            _writer = new StreamWriter(new FileStream(CurrentFilePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read));
            _writer.WriteLine("timestamp_utc,event,detail");
            _writer.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SessionLog] Unable to start: {ex}");
            _writer?.Dispose();
            _writer = null;
            CurrentFilePath = null;
        }
    }

    public void Write(string eventName, string detail = "")
    {
        try
        {
            if (_writer == null) return;
            _writer.WriteLine($"{DateTime.UtcNow:O},{Escape(eventName)},{Escape(detail)}");
            _writer.Flush();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[SessionLog] Unable to write: {ex}");
        }
    }

    public void Stop()
    {
        try { _writer?.Dispose(); }
        catch (Exception ex) { Debug.WriteLine($"[SessionLog] Unable to close: {ex}"); }
        _writer = null;
    }

    public void Dispose() => Stop();

    private static string Escape(string value) => $"\"{value.Replace("\"", "\"\"")}\"";
}
