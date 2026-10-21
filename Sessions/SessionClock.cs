using System;
using System.Diagnostics;

namespace ErgTrainer.Sessions;

public sealed class SessionClock
{
    private readonly Stopwatch _watch = new();
    private TrainingSession? _session;
    private bool _started;

    public bool IsActive => _started;
    public bool IsRunning => _started && _watch.IsRunning;
    public TimeSpan Elapsed => _watch.Elapsed;

    public void Start(TrainingSession session)
    {
        if (session.Validate().Count != 0) throw new ArgumentException("Invalid session.", nameof(session));
        _session = session;
        _watch.Restart();
        _started = true;
    }

    public void Pause() { if (_started) _watch.Stop(); }
    public void Resume() { if (_started) _watch.Start(); }
    public void Stop() { _watch.Reset(); _started = false; _session = null; }

    public SessionPosition Position => Locate(_session, _watch.Elapsed);

    public static SessionPosition Locate(TrainingSession? session, TimeSpan elapsed)
    {
        if (session?.Blocks is not { Count: > 0 }) return new(-1, 0, 0, true);
        double seconds = Math.Max(0, elapsed.TotalSeconds);
        double start = 0;
        for (int i = 0; i < session.Blocks.Count; i++)
        {
            double end = start + session.Blocks[i].DurationSeconds;
            if (seconds < end) return new(i, seconds - start, seconds, false);
            start = end;
        }
        return new(session.Blocks.Count - 1, session.Blocks[^1].DurationSeconds, start, true);
    }
}

public readonly record struct SessionPosition(int BlockIndex, double BlockElapsedSeconds,
    double TotalElapsedSeconds, bool Finished);
