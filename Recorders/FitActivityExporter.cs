using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Dynastream.Fit;
using FitTime = Dynastream.Fit.DateTime;

namespace ErgTrainer.Recorders;

public static class FitActivityExporter
{
    public static void Export(string journalPath, string fitPath)
    {
        var entries = new List<ActivityEntry>();
        foreach (string line in System.IO.File.ReadLines(journalPath))
            entries.Add(JsonSerializer.Deserialize<ActivityEntry>(line)
                ?? throw new InvalidDataException("Invalid activity journal entry."));
        if (entries.Count < 2 || entries[0].Kind != "start" || entries[^1].Kind != "end")
            throw new InvalidDataException("The activity journal is incomplete.");

        var started = entries[0];
        var ended = entries[^1];
        double elapsed = (ended.TimestampUtc - started.TimestampUtc).TotalSeconds;
        double timer = ended.ActiveSeconds ?? 0;
        if (elapsed < 0 || timer < 0 || timer > elapsed + 1)
            throw new InvalidDataException("Activity timing is invalid.");

        // Finalize and check a temporary FIT before making it available for import.
        // File.Move without overwrite preserves any earlier export.
        string tempPath = fitPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        using var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
        var encoder = new Encode(ProtocolVersion.V20);
        try
        {
            encoder.Open(stream);
            var id = new FileIdMesg();
            id.SetType(Dynastream.Fit.File.Activity);
            id.SetManufacturer(Manufacturer.Development);
            id.SetProduct(1);
            id.SetSerialNumber(BitConverter.ToUInt32(SHA256.HashData(
                Encoding.UTF8.GetBytes(Path.GetFileName(journalPath))), 0));
            id.SetTimeCreated(Time(started.TimestampUtc));
            encoder.Write(id);

            double lapStartTimer = 0;
            DateTimeOffset lapStartUtc = started.TimestampUtc;
            int lapCount = 0;
            int recordCount = 0;
            foreach (var entry in entries)
            {
                switch (entry.Kind)
                {
                    case "start":
                    case "resume":
                        WriteTimerEvent(encoder, entry.TimestampUtc, EventType.Start);
                        break;
                    case "pause":
                    case "disconnect":
                    case "end":
                        WriteTimerEvent(encoder, entry.TimestampUtc, EventType.StopAll);
                        break;
                    case "sample":
                        var record = new RecordMesg();
                        record.SetTimestamp(Time(entry.TimestampUtc));
                        bool hasValue = false;
                        if (entry.PowerWatts is >= 0 and <= ushort.MaxValue)
                        {
                            record.SetPower((ushort)Math.Round(entry.PowerWatts.Value));
                            hasValue = true;
                        }
                        if (entry.CadenceRpm is >= 0 and <= byte.MaxValue)
                        {
                            record.SetCadence((byte)Math.Round(entry.CadenceRpm.Value));
                            hasValue = true;
                        }
                        if (entry.SpeedKph is >= 0 and <= 300)
                        {
                            record.SetSpeed((float)(entry.SpeedKph.Value / 3.6));
                            hasValue = true;
                        }
                        if (entry.HeartRateBpm is > 0 and <= byte.MaxValue)
                        {
                            record.SetHeartRate((byte)entry.HeartRateBpm.Value);
                            hasValue = true;
                        }
                        if (hasValue)
                        {
                            encoder.Write(record);
                            recordCount++;
                        }
                        break;
                    case "block":
                        if (entry.ActiveSeconds is double blockTime && blockTime > lapStartTimer)
                        {
                            WriteLap(encoder, lapCount++, lapStartUtc, entry.TimestampUtc,
                                blockTime - lapStartTimer);
                            lapStartUtc = entry.TimestampUtc;
                            lapStartTimer = blockTime;
                        }
                        break;
                }
            }
            if (recordCount == 0)
                throw new InvalidDataException("No measured telemetry was captured for a FIT record.");
            WriteLap(encoder, lapCount++, lapStartUtc, ended.TimestampUtc,
                Math.Max(0, timer - lapStartTimer));

            var session = new SessionMesg();
            session.SetMessageIndex(0);
            session.SetTimestamp(Time(ended.TimestampUtc));
            session.SetStartTime(Time(started.TimestampUtc));
            session.SetTotalElapsedTime((float)elapsed);
            session.SetTotalTimerTime((float)timer);
            session.SetSport(Sport.Cycling);
            session.SetSubSport(SubSport.IndoorCycling);
            session.SetFirstLapIndex(0);
            session.SetNumLaps((ushort)lapCount);
            encoder.Write(session);

            var activity = new ActivityMesg();
            activity.SetTimestamp(Time(ended.TimestampUtc));
            activity.SetNumSessions(1);
            int utcSeconds = (int)Time(ended.TimestampUtc).GetTimeStamp();
            int offsetSeconds = (int)TimeZoneInfo.Local.GetUtcOffset(ended.TimestampUtc.UtcDateTime).TotalSeconds;
            activity.SetLocalTimestamp((uint)(utcSeconds + offsetSeconds));
            activity.SetTotalTimerTime((float)timer);
            activity.SetEvent(Event.Activity);
            activity.SetEventType(EventType.Stop);
            encoder.Write(activity);
            encoder.Close();
            stream.Flush(true);
            stream.Dispose();
            using (var checkStream = System.IO.File.OpenRead(tempPath))
            {
                if (!new Decode().CheckIntegrity(checkStream))
                    throw new InvalidDataException("FIT SDK integrity check failed.");
            }
            System.IO.File.Move(tempPath, fitPath, false);
        }
        finally
        {
            stream.Dispose();
            if (System.IO.File.Exists(tempPath)) System.IO.File.Delete(tempPath);
        }
    }

    private static FitTime Time(DateTimeOffset value) => new(value.UtcDateTime);

    private static void WriteTimerEvent(Encode encoder, DateTimeOffset when, EventType type)
    {
        var message = new EventMesg();
        message.SetTimestamp(Time(when));
        message.SetEvent(Event.Timer);
        message.SetEventType(type);
        encoder.Write(message);
    }

    private static void WriteLap(Encode encoder, int index, DateTimeOffset start,
        DateTimeOffset end, double timerSeconds)
    {
        var lap = new LapMesg();
        lap.SetMessageIndex((ushort)index);
        lap.SetTimestamp(Time(end));
        lap.SetStartTime(Time(start));
        lap.SetTotalElapsedTime((float)Math.Max(0, (end - start).TotalSeconds));
        lap.SetTotalTimerTime((float)timerSeconds);
        lap.SetSport(Sport.Cycling);
        lap.SetSubSport(SubSport.IndoorCycling);
        encoder.Write(lap);
    }
}
