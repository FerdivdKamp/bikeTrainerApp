using System;
using System.IO;
using ErgTrainer.Sessions;
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
