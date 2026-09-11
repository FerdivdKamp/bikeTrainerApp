namespace ErgTrainer.Recorders;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using ErgTrainer.Sensors;

public class DataRecorder
{

    private List<TrainerData> _recordingBuffer = new();
    private bool _isRecording = false;
    private DateTime _recordingStartTime;
    private string _currentRecordingPath = "";
    private StreamWriter _writer;
    private readonly object _lockObject = new();

    public bool IsRecording => _isRecording;

    public async Task StartRecordingAsync(string basePath = "recordings")
    {
        if (_isRecording) return;

        // Create directory if it doesn't exist
        if (!Directory.Exists(basePath))
        {
            Directory.CreateDirectory(basePath);
        }

        // Generate unique filename with timestamp
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _currentRecordingPath = Path.Combine(basePath, $"recording_{timestamp}.csv");
        
        _recordingStartTime = DateTime.Now;
        _isRecording = true;
        _recordingBuffer.Clear();

        // Create and initialize the CSV file
        using var file = File.Create(_currentRecordingPath);
        file.Close();

        // Write header
        _writer = new StreamWriter(_currentRecordingPath);
        await _writer.WriteLineAsync("Timestamp,Power(W),Cadence(RPM),Speed(KPH)");
        
        Console.WriteLine($"Started recording to {_currentRecordingPath}");
    }

    public void StopRecording()
    {
        if (!_isRecording) return;

        _isRecording = false;
        
        // Flush and close writer
        _writer?.Flush();
        _writer?.Close();
        _writer = null;

        Console.WriteLine($"Stopped recording. Data saved to {_currentRecordingPath}");
    }

    public void AddData(TrainerData data)
    {
        if (!_isRecording) return;

        lock (_lockObject)
        {
            // Add to buffer for potential offline processing
            _recordingBuffer.Add(data);

            // Write to file immediately
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            var line = $"{timestamp},{data.PowerWatts:F1},{data.CadenceRpm:F1},{data.SpeedKph:F1}";
            
            _writer?.WriteLine(line);
        }
    }

    public List<TrainerData> GetRecordingBuffer()
    {
        lock (_lockObject)
        {
            return new List<TrainerData>(_recordingBuffer);
        }
    }

    public string GetCurrentRecordingPath()
    {
        return _currentRecordingPath;
    }
}

public class TacxDevice
{
    private DataRecorder _dataRecorder = new();
    private TacxDevice _device = new();
    private bool _isConnected = false;

    public async Task StartRecordingAsync()
    {
        await _dataRecorder.StartRecordingAsync();
    }

    public void StopRecording()
    {
        _dataRecorder.StopRecording();
    }

    public bool IsRecording => _dataRecorder.IsRecording;

    public void OnDataReceived(TrainerData data)
    {
        // Process data normally
        _device.ProcessData(data);
        
        // Record data if recording is active
        if (_dataRecorder.IsRecording)
        {
            _dataRecorder.AddData(data);
        }
    }

    private void ProcessData(TrainerData data)
    {
        // Your existing data processing logic here
        // This method would contain your existing logic
    }
}
