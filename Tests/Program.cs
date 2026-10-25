using System;
using System.IO;
using ErgTrainer.Sessions;
using ErgTrainer.Recorders;
using Decode = Dynastream.Fit.Decode;
using RecordMesg = Dynastream.Fit.RecordMesg;
using LapMesg = Dynastream.Fit.LapMesg;
using SessionMesg = Dynastream.Fit.SessionMesg;
using ActivityMesg = Dynastream.Fit.ActivityMesg;
using EventMesg = Dynastream.Fit.EventMesg;
using Event = Dynastream.Fit.Event;
using MesgNum = Dynastream.Fit.MesgNum;
using ErgTrainer.Sensors;

static void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}

var erg = new TrainingSession { Name = "Intervals" };
erg.Blocks.Add(new TrainingBlock { Name = "Easy", DurationSeconds = 60, TargetWatts = 100, Instruction = "Easy" });
erg.Blocks.Add(new TrainingBlock { Name = "Hard", DurationSeconds = 10, TargetWatts = 300, Category = "sprint" });
Check(erg.Validate().Count == 0, "Valid ERG session rejected.");
Check(SessionClock.Locate(erg, TimeSpan.FromSeconds(59.9)).BlockIndex == 0, "Early transition.");
var boundary = SessionClock.Locate(erg, TimeSpan.FromSeconds(60));
Check(boundary.BlockIndex == 1 && boundary.BlockElapsedSeconds == 0, "Block boundary is incorrect.");
Check(SessionClock.Locate(erg, TimeSpan.FromSeconds(70)).Finished, "Session should finish at its end.");

var simulation = new TrainingSession { Name = "Hills", Mode = "simulation" };
simulation.Blocks.Add(new TrainingBlock { Name = "Descent", DurationSeconds = 30, GradientPercent = -5 });
Check(simulation.Validate().Count == 0, "Valid negative gradient rejected.");
Check(Convert.ToHexString(FecSimulationPackets.UserConfiguration(75, 10, 2105)) ==
      "A4094F05374C1DFF800C4342F3", "FE-C user configuration bytes changed.");
Check(Convert.ToHexString(FecSimulationPackets.NeutralWind()) ==
      "A4094F0532FFFFFFFF007F64CE", "FE-C neutral wind bytes changed.");
Check(Convert.ToHexString(FecSimulationPackets.RequestCommandStatus()) ==
      "A4094F0546FFFFFFFF80470167", "FE-C status request bytes changed.");
Check(Convert.ToHexString(FecSimulationPackets.TrackResistance(3)) ==
      "A4094F0533FFFFFFFF4C4F5087", "FE-C 3% slope bytes changed.");
byte[] statusFrame = { 0xA4, 0x09, 0x4F, 0x05, 0x47, 0x33, 0x01, 0x00,
    0xFF, 0xFF, 0xFF, 0xFF, 0 };
for (int i = 0; i < 12; i++) statusFrame[12] ^= statusFrame[i];
Check(FecSimulationPackets.TryReadCommandStatus(statusFrame, 0x33, out byte commandStatus) && commandStatus == 0,
    "FE-C slope status was not matched.");
Check(!FecSimulationPackets.TryReadCommandStatus(statusFrame, 0x31, out _),
    "FE-C slope status was mistaken for ERG.");
statusFrame[5] = 0x31;
statusFrame[12] ^= 0x33 ^ 0x31;
Check(FecSimulationPackets.TryReadCommandStatus(statusFrame, 0x31, out commandStatus) && commandStatus == 0,
    "FE-C ERG status was not matched.");
statusFrame[12] ^= 1;
Check(!FecSimulationPackets.TryReadCommandStatus(statusFrame, 0x31, out _),
    "Invalid FE-C checksum was accepted.");
simulation.RiderWeightKg = 82.5;
simulation.BikeWeightKg = 9.8;
Check(simulation.Validate().Count == 0, "Valid simulation weights rejected.");
simulation.RiderWeightKg = 0;
Check(simulation.Validate().Count > 0, "Zero rider weight accepted.");
simulation.RiderWeightKg = 82.5;
simulation.BikeWeightKg = 51;
Check(simulation.Validate().Count > 0, "Out-of-range bike weight accepted.");
simulation.BikeWeightKg = 9.8;
simulation.Blocks[0].TargetWatts = 100;
Check(simulation.Validate().Count > 0, "Mixed-mode block accepted.");
simulation.Blocks[0].TargetWatts = null;
simulation.Blocks[0].GradientPercent = 21;
Check(simulation.Validate().Count > 0, "Unsupported gradient accepted.");
simulation.Blocks[0].GradientPercent = -5;
erg.Blocks[1].DurationSeconds = 0;
Check(erg.Validate().Count > 0, "Zero-duration block accepted.");
erg.Blocks[1].DurationSeconds = 10;

string path = Path.Combine(Path.GetTempPath(), $"ergtrainer-session-check-{Guid.NewGuid():N}.ergsession.json");
try
{
    await TrainingSessionFiles.SaveAsync(path, erg);
    var loaded = await TrainingSessionFiles.LoadAsync(path);
    Check(loaded.Mode == "erg" && loaded.Blocks[1].TargetWatts == 300 &&
          loaded.Blocks[0].Instruction == "Easy" && loaded.TotalSeconds == 70,
        "JSON round trip lost session data.");
    await TrainingSessionFiles.SaveAsync(path, simulation);
    loaded = await TrainingSessionFiles.LoadAsync(path);
    Check(loaded.RiderWeightKg == 82.5 && loaded.BikeWeightKg == 9.8,
        "Simulation weight round trip lost data.");
    await File.WriteAllTextAsync(path,
        "{\"schemaVersion\":1,\"name\":\"Old hills\",\"mode\":\"simulation\",\"blocks\":[{\"name\":\"Hill\",\"durationSeconds\":60,\"gradientPercent\":3}]}");
    loaded = await TrainingSessionFiles.LoadAsync(path);
    Check(loaded.RiderWeightKg == 75 && loaded.BikeWeightKg == 10,
        "An older simulation without weights did not receive defaults.");
    await File.WriteAllTextAsync(path, "{\"name\":\"Missing schema\",\"mode\":\"erg\",\"blocks\":[]}");
    try
    {
        await TrainingSessionFiles.LoadAsync(path);
        throw new Exception("Missing schema version was accepted.");
    }
    catch (System.Text.Json.JsonException) { }
}
finally
{
    if (File.Exists(path)) File.Delete(path);
}

Console.WriteLine("Session model, timing, validation, and JSON checks passed.");

string activityDirectory = Path.Combine(Path.GetTempPath(), $"ergtrainer-fit-check-{Guid.NewGuid():N}");
Directory.CreateDirectory(activityDirectory);
try
{
    var start = new DateTimeOffset(2026, 10, 25, 12, 0, 0, TimeSpan.Zero);
    var recorder = new ActivityRecorder("Intervals", start, activityDirectory);
    Check(recorder.Add(new ActivityEntry("sample", start.AddSeconds(1),
        PowerWatts: 180, CadenceRpm: 85, SpeedKph: 27)), "First sample was rejected.");
    recorder.Add(new ActivityEntry("pause", start.AddSeconds(5), ActiveSeconds: 5));
    recorder.Add(new ActivityEntry("resume", start.AddSeconds(15), ActiveSeconds: 5));
    recorder.Add(new ActivityEntry("sample", start.AddSeconds(16),
        PowerWatts: 190, HeartRateBpm: 145));
    recorder.Add(new ActivityEntry("block", start.AddSeconds(20), "Hard", 1,
        ActiveSeconds: 10));
    recorder.Add(new ActivityEntry("disconnect", start.AddSeconds(21), ActiveSeconds: 11));
    recorder.Add(new ActivityEntry("resume", start.AddSeconds(24), ActiveSeconds: 11));
    string fitPath = await recorder.FinishAsync(start.AddSeconds(30), 17, "complete");
    Check(File.Exists(recorder.JournalPath), "Activity journal was lost after export.");
    using var stream = File.OpenRead(fitPath);
    var decode = new Decode();
    Check(decode.CheckIntegrity(stream), "FIT SDK integrity check failed.");
    int records = 0, laps = 0, sessions = 0, activities = 0, timerEvents = 0;
    decode.MesgEvent += (_, e) =>
    {
        if (e.mesg.Num == MesgNum.Record)
        {
            var record = new RecordMesg(e.mesg);
            if (records == 0)
            {
                Check(record.GetHeartRate() == null,
                    "Missing first heart-rate reading became a value.");
                Check(record.GetPower() == 180 && record.GetCadence() == 85 &&
                    Math.Abs((record.GetSpeed() ?? 0) - 7.5f) < 0.01,
                    "Measured trainer values or speed units changed in FIT.");
                Check(record.GetPositionLat() == null, "FIT contains a fabricated GPS point.");
            }
            records++;
        }
        if (e.mesg.Num == MesgNum.Lap) laps++;
        if (e.mesg.Num == MesgNum.Session)
        {
            var session = new SessionMesg(e.mesg);
            sessions++;
            Check(Math.Abs((session.GetTotalElapsedTime() ?? 0) - 30) < 0.01,
                "FIT elapsed time is wrong.");
            Check(Math.Abs((session.GetTotalTimerTime() ?? 0) - 17) < 0.01,
                "FIT active time is wrong.");
        }
        if (e.mesg.Num == MesgNum.Activity) activities++;
        if (e.mesg.Num == MesgNum.Event && new EventMesg(e.mesg).GetEvent() == Event.Timer)
            timerEvents++;
    };
    Check(decode.Read(stream), "FIT SDK could not decode activity.");
    Check(records == 2 && laps == 2 && sessions == 1 && activities == 1 && timerEvents == 6,
        $"FIT activity message counts are wrong: {records} records, {laps} laps, " +
        $"{sessions} sessions, {activities} activities, {timerEvents} timer events.");

    var existingFit = File.ReadAllBytes(fitPath);
    try
    {
        FitActivityExporter.Export(recorder.JournalPath, fitPath);
        throw new Exception("An existing FIT export was overwritten.");
    }
    catch (IOException) { }
    Check(File.ReadAllBytes(fitPath).AsSpan().SequenceEqual(existingFit),
        "An existing FIT export was changed.");

    var empty = new ActivityRecorder("No telemetry", start, activityDirectory);
    try
    {
        await empty.FinishAsync(start.AddSeconds(2), 2, "stopped");
        throw new Exception("FIT export invented a record for a session without telemetry.");
    }
    catch (InvalidDataException) { }
    Check(File.Exists(empty.JournalPath) && !File.Exists(empty.FitPath),
        "A telemetry-free session lost its journal or produced a FIT file.");
}
finally
{
    foreach (string file in Directory.GetFiles(activityDirectory)) File.Delete(file);
    Directory.Delete(activityDirectory);
}

Console.WriteLine("FIT activity encoding, pause timing, and SDK integrity checks passed.");
