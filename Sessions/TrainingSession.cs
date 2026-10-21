using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.IO;

namespace ErgTrainer.Sessions;

public sealed class TrainingSession
{
    [JsonRequired]
    public int SchemaVersion { get; set; } = 1;
    [JsonRequired]
    public string Name { get; set; } = "New session";
    public string Description { get; set; } = "";
    [JsonRequired]
    public string Mode { get; set; } = "erg";
    // Defaults keep sessions saved before weight fields were added loadable.
    public double RiderWeightKg { get; set; } = 75;
    public double BikeWeightKg { get; set; } = 10;
    [JsonRequired]
    public List<TrainingBlock> Blocks { get; set; } = new();

    public long TotalSeconds => Blocks?.Sum(b => (long)b.DurationSeconds) ?? 0;

    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();
        if (SchemaVersion != 1) errors.Add($"Unsupported schema version {SchemaVersion}.");
        if (string.IsNullOrWhiteSpace(Name)) errors.Add("Enter a session name.");
        if (Mode is not ("erg" or "simulation")) errors.Add("Mode must be erg or simulation.");
        if (!double.IsFinite(RiderWeightKg) || RiderWeightKg <= 0 || RiderWeightKg > 655.34)
            errors.Add("Rider weight must be greater than 0 and at most 655.34 kg.");
        if (!double.IsFinite(BikeWeightKg) || BikeWeightKg <= 0 || BikeWeightKg > 50)
            errors.Add("Bike weight must be greater than 0 and at most 50 kg.");
        if (Blocks == null || Blocks.Count == 0)
        {
            errors.Add("Add at least one block.");
            return errors;
        }

        long total = 0;
        for (int i = 0; i < Blocks.Count; i++)
        {
            var block = Blocks[i];
            if (block == null)
            {
                errors.Add($"Block {i + 1} is missing.");
                continue;
            }
            if (string.IsNullOrWhiteSpace(block.Name)) errors.Add($"Block {i + 1} needs a name.");
            if (block.DurationSeconds <= 0) errors.Add($"Block {i + 1} needs a positive duration.");
            total += block.DurationSeconds;
            if (Mode == "erg")
            {
                if (block.TargetWatts is not >= 0 and <= 1000 || block.GradientPercent != null)
                    errors.Add($"Block {i + 1} needs 0–1000 W and no gradient.");
            }
            else if (Mode == "simulation")
            {
                if (block.GradientPercent is not double gradient || !double.IsFinite(gradient) ||
                    gradient < -20 || gradient > 20 || block.TargetWatts != null)
                    errors.Add($"Block {i + 1} needs a gradient from -20% to 20% and no watt target.");
            }
        }
        if (total > 24 * 60 * 60) errors.Add("A session may last at most 24 hours.");
        return errors;
    }
}

public sealed class TrainingBlock
{
    [JsonRequired]
    public string Name { get; set; } = "Block";
    [JsonRequired]
    public int DurationSeconds { get; set; } = 60;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public int? TargetWatts { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public double? GradientPercent { get; set; }
    public string Category { get; set; } = "custom";
    public string Instruction { get; set; } = "";

    public TrainingBlock Copy() => new()
    {
        Name = Name, DurationSeconds = DurationSeconds, TargetWatts = TargetWatts,
        GradientPercent = GradientPercent, Category = Category, Instruction = Instruction
    };
}

public static class TrainingSessionFiles
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.Strict
    };

    public static async Task<TrainingSession> LoadAsync(string path, CancellationToken token = default)
    {
        await using var stream = File.OpenRead(path);
        var session = await JsonSerializer.DeserializeAsync<TrainingSession>(stream, Options, token)
            ?? throw new InvalidDataException("The file does not contain a session.");
        var errors = session.Validate();
        if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        return session;
    }

    public static async Task SaveAsync(string path, TrainingSession session, CancellationToken token = default)
    {
        var errors = session.Validate();
        if (errors.Count != 0) throw new InvalidDataException(string.Join(Environment.NewLine, errors));
        // A new file is written first, so validation or serialization cannot truncate an existing session.
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                await JsonSerializer.SerializeAsync(stream, session, Options, token);
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
