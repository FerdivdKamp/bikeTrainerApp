using System;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace ErgTrainer.Recorders;

// The journal is kept even after FIT export, so a failed export or abrupt exit
// does not erase the measured data that has already reached the writer.
public sealed record ActivityEntry(
    string Kind, DateTimeOffset TimestampUtc, string? Name = null, int? BlockIndex = null,
    double? PowerWatts = null, double? CadenceRpm = null, double? SpeedKph = null,
    int? HeartRateBpm = null, double? ActiveSeconds = null);

public sealed class ActivityRecorder
{
    private readonly Channel<ActivityEntry> _entries = Channel.CreateBounded<ActivityEntry>(
        new BoundedChannelOptions(256) { SingleReader = true, SingleWriter = true, FullMode = BoundedChannelFullMode.Wait });
    private readonly Task _writerTask;
    private bool _closed;
    private Exception? _failure;

    public string JournalPath { get; }
    public string FitPath => Path.ChangeExtension(JournalPath, ".fit");
    public string? Error => _failure?.Message;

    public ActivityRecorder(string name, DateTimeOffset startedUtc, string? directory = null)
    {
        directory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "ErgTrainer", "Activities");
        Directory.CreateDirectory(directory);
        JournalPath = Path.Combine(directory,
            $"activity-{startedUtc:yyyyMMdd-HHmmssfff}-{Guid.NewGuid():N}.jsonl");
        var stream = new FileStream(JournalPath, FileMode.CreateNew, FileAccess.Write,
            FileShare.Read, 4096, FileOptions.Asynchronous);
        _writerTask = WriteJournalAsync(stream);
        Add(new ActivityEntry("start", startedUtc, name));
    }

    public bool Add(ActivityEntry entry)
    {
        if (_closed || _failure != null) return false;
        if (_entries.Writer.TryWrite(entry)) return true;
        _failure = new IOException("Activity recorder fell behind; the journal was stopped to avoid losing samples silently.");
        _entries.Writer.TryComplete();
        return false;
    }

    public async Task<string> FinishAsync(DateTimeOffset endedUtc, double activeSeconds, string reason)
    {
        if (!_closed)
        {
            Add(new ActivityEntry("end", endedUtc, reason, ActiveSeconds: activeSeconds));
            _closed = true;
            _entries.Writer.TryComplete();
        }
        await _writerTask;
        if (_failure != null) throw new IOException("Activity journal is incomplete.", _failure);
        await Task.Run(() => FitActivityExporter.Export(JournalPath, FitPath));
        return FitPath;
    }

    private async Task WriteJournalAsync(Stream stream)
    {
        try
        {
            await using (stream)
            await using (var writer = new StreamWriter(stream))
            {
                int pending = 0;
                await foreach (var entry in _entries.Reader.ReadAllAsync())
                {
                    await writer.WriteLineAsync(JsonSerializer.Serialize(entry));
                    if (++pending >= 5 || entry.Kind != "sample")
                    {
                        await writer.FlushAsync();
                        pending = 0;
                    }
                }
                await writer.FlushAsync();
            }
        }
        catch (Exception ex)
        {
            _failure = ex;
            _entries.Writer.TryComplete();
            Debug.WriteLine($"[ActivityRecorder] Journal write failed: {ex}");
        }
    }
}
