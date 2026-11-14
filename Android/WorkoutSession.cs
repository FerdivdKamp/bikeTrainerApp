using System.Globalization;
using System.Text;
using ErgTrainer.Sessions;
using InTheHand.Bluetooth;

namespace ErgTrainer.AndroidApp;

public partial class MainActivity
{
    private void TrainerControlDiagnostic(string message) =>
        global::Android.Util.Log.Info("ErgTrainer", message);
    private readonly SessionClock sessionClock = new();
    private readonly SemaphoreSlim recordingGate = new(1, 1);
    private TrainingSession? runningWorkout;
    private CancellationTokenSource? sessionCancellation;
    private StreamWriter? recording;
    private bool sessionStarting;
    private bool sessionCommandPending;
    private int sessionGeneration;
    private int lastBlock = -1;
    private string workoutSnapshotFile = "";

    private async Task DisconnectTrainerAsync()
    {
        if (sessionClock.IsActive || sessionStarting || sessionCommandPending)
        { status.Text = "Stop the session before disconnecting the trainer."; return; }
        if (connectedTrainer != null) connectedTrainer.GattServerDisconnected -= TrainerDisconnected;
        connectedTrainer = null;
        latestTrainerData = null;
        trainerReading = "Power: -- W\nCadence: -- rpm\nSpeed: -- km/h";
        readings.Text = trainerReading + "\n" + heartReading;
        try { await trainer.DisconnectAsync(); status.Text = "Trainer disconnected."; }
        catch (Exception ex) { status.Text = $"Disconnect failed: {ex.Message}"; }
    }

    private async Task DisconnectHeartRateAsync()
    {
        if (connectedHeartRate != null) connectedHeartRate.GattServerDisconnected -= HeartRateDisconnected;
        connectedHeartRate = null;
        latestHeartRate = null;
        heartReading = "Heart rate: -- bpm";
        readings.Text = trainerReading + "\n" + heartReading;
        try { await heartRate.DisconnectAsync(); status.Text = "Heart-rate monitor disconnected."; }
        catch (Exception ex) { status.Text = $"Disconnect failed: {ex.Message}"; }
    }

    private void HeartRateDisconnected(object? sender, EventArgs e)
    {
        if (destroyed) return;
        RunOnUiThread(() =>
        {
            if (destroyed || !ReferenceEquals(sender, connectedHeartRate)) return;
            connectedHeartRate!.GattServerDisconnected -= HeartRateDisconnected;
            connectedHeartRate = null;
            global::Android.Util.Log.Warn("ErgTrainer", "Heart-rate monitor disconnected.");
            latestHeartRate = null;
            heartReading = "Heart rate: -- bpm";
            readings.Text = trainerReading + "\n" + heartReading;
            status.Text = "Heart-rate monitor disconnected. Reconnect it on the Connect page.";
            RefreshSessionDisplay();
        });
    }

    private void TrainerDisconnected(object? sender, EventArgs e)
    {
        if (destroyed) return;
        RunOnUiThread(async () =>
        {
            if (destroyed || !ReferenceEquals(sender, connectedTrainer)) return;
            connectedTrainer!.GattServerDisconnected -= TrainerDisconnected;
            connectedTrainer = null;
            global::Android.Util.Log.Warn("ErgTrainer", "Trainer disconnected.");
            latestTrainerData = null;
            trainerReading = "Power: -- W\nCadence: -- rpm\nSpeed: -- km/h";
            readings.Text = trainerReading + "\n" + heartReading;
            status.Text = "Trainer disconnected. Reconnect it on the Connect page.";
            if (sessionClock.IsActive || sessionStarting)
                await StopSessionAsync("Trainer disconnected. Ride saved; reconnect before starting again.");
        });
    }

    private void RefreshSessionDisplay()
    {
        if (sessionDisplay == null) return;
        var plan = runningWorkout ?? SnapshotWorkout();
        var position = sessionClock.Position;
        string detail = plan.Blocks.Count == 0 ? "No blocks yet." :
            sessionClock.IsActive && position.BlockIndex >= 0
                ? $"Block {position.BlockIndex + 1}/{plan.Blocks.Count}: {plan.Blocks[position.BlockIndex].Name}\n" +
                  $"Target: {plan.Blocks[position.BlockIndex].TargetWatts} W\n" +
                  $"Block: {TimeSpan.FromSeconds(position.BlockElapsedSeconds):hh\\:mm\\:ss} / " +
                  $"{TimeSpan.FromSeconds(plan.Blocks[position.BlockIndex].DurationSeconds):hh\\:mm\\:ss}\n" +
                  plan.Blocks[position.BlockIndex].Instruction
                : $"{plan.Blocks.Count} blocks, {TimeSpan.FromSeconds(plan.TotalSeconds):hh\\:mm\\:ss}";
        sessionDisplay.Text = $"{plan.Name}\n{detail}\n\n{trainerReading}\n{heartReading}";
        startButton.Enabled = !sessionClock.IsActive && !sessionStarting;
        pauseButton.Enabled = sessionClock.IsActive && !sessionStarting && !sessionCommandPending;
        pauseButton.Text = sessionClock.IsRunning ? "Pause" : "Resume";
        stopButton.Enabled = sessionClock.IsActive || sessionStarting;
    }

    private async Task StartSessionAsync()
    {
        if (sessionStarting || sessionClock.IsActive || sessionCommandPending) return;
        var plan = SnapshotWorkout();
        var errors = plan.Validate();
        if (errors.Count > 0) { sessionStatus.Text = string.Join("\n", errors); return; }
        if (plan.Mode != "erg") { sessionStatus.Text = "Simulation workouts cannot be started yet."; return; }
        if (connectedTrainer == null || trainer.ControlMethod != "Tacx FE-C over BLE")
        { sessionStatus.Text = "Connect a trainer with Tacx FE-C over BLE control first."; return; }
        sessionStarting = true;
        int generation = ++sessionGeneration;
        sessionCancellation = CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
        var token = sessionCancellation.Token;
        RefreshSessionDisplay();
        sessionStatus.Text = "Starting: saving the workout and setting the first target…";
        try
        {
            // Save a snapshot for every ride, even when the workout was never explicitly saved.
            Directory.CreateDirectory(WorkoutDirectory);
            var snapshotPath = Path.Combine(WorkoutDirectory, Guid.NewGuid().ToString("N") + ".ergsession.json");
            await TrainingSessionFiles.SaveAsync(snapshotPath, plan, token);
            workoutSnapshotFile = Path.GetFileName(snapshotPath);
            token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(RecordingDirectory);
            string path = Path.Combine(RecordingDirectory, $"ride-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.csv");
            await recordingGate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                recording = new StreamWriter(new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.Read,
                    4096, useAsync: true), new UTF8Encoding(false));
                await recording.WriteLineAsync("timestamp_utc,elapsed_seconds,block,power_w,cadence_rpm,speed_kph,heart_rate_bpm,event,workout_file");
                await recording.FlushAsync();
            }
            finally { recordingGate.Release(); }
            token.ThrowIfCancellationRequested();
            var result = await trainer.SetTargetPowerWithResultAsync(plan.Blocks[0].TargetWatts!.Value, token);
            if (generation != sessionGeneration) return;
            if (!result.Accepted) { await StopSessionAsync($"First target was not confirmed: {result.Message}"); return; }
            runningWorkout = plan;
            lastBlock = 0;
            sessionClock.Start(plan);
            sessionStarting = false;
            await RecordAsync("started", token);
            sessionStatus.Text = "Session running. Keep the app open.";
            RefreshSessionDisplay();
            _ = RunSessionAsync(generation, token);
        }
        catch (System.OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation == sessionGeneration)
                await StopSessionAsync($"Could not start session: {ex.Message}");
        }
    }

    private async Task RunSessionAsync(int generation, CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested && generation == sessionGeneration)
            {
                await Task.Delay(1000, token);
                if (!sessionClock.IsRunning) continue;
                var position = sessionClock.Position;
                if (position.Finished)
                {
                    await StopSessionAsync("Session complete. The trainer retains its last target.");
                    return;
                }
                if (position.BlockIndex != lastBlock)
                {
                    // Pause while sending a new target so the block does not advance during I/O.
                    sessionCommandPending = true;
                    sessionClock.Pause();
                    RefreshSessionDisplay();
                    var result = await trainer.SetTargetPowerWithResultAsync(
                        runningWorkout!.Blocks[position.BlockIndex].TargetWatts!.Value, token);
                    if (generation != sessionGeneration) return;
                    sessionCommandPending = false;
                    if (!result.Accepted)
                    {
                        await StopSessionAsync($"Block target was not confirmed: {result.Message}");
                        return;
                    }
                    lastBlock = position.BlockIndex;
                    sessionClock.Resume();
                    await RecordAsync("block", token);
                }
                else await RecordAsync("", token);
                RefreshSessionDisplay();
            }
        }
        catch (System.OperationCanceledException) { }
        catch (Exception ex)
        {
            if (generation == sessionGeneration && !destroyed)
                await StopSessionAsync($"Session stopped: {ex.Message}");
        }
    }

    private async void PauseOrResume()
    {
        try
        {
            if (!sessionClock.IsActive || sessionStarting || sessionCommandPending) return;
            if (sessionClock.IsRunning)
            {
                sessionClock.Pause();
                sessionStatus.Text = "Paused. The trainer retains its current target.";
                await RecordAsync("paused", sessionCancellation?.Token ?? default);
            }
            else
            {
                if (connectedTrainer == null) { sessionStatus.Text = "Reconnect the trainer first."; return; }
                sessionClock.Resume();
                sessionStatus.Text = "Session running.";
                await RecordAsync("resumed", sessionCancellation?.Token ?? default);
            }
            RefreshSessionDisplay();
        }
        catch (System.OperationCanceledException) { }
        catch (Exception ex) { await StopSessionAsync($"Recording failed: {ex.Message}"); }
    }

    private async Task RecordAsync(string eventName, CancellationToken token)
    {
        await recordingGate.WaitAsync(token);
        try
        {
            if (recording == null) return;
            var d = latestTrainerData;
            string Number(double? value) => value?.ToString("0.###", CultureInfo.InvariantCulture) ?? "";
            string line = string.Join(",", DateTimeOffset.UtcNow.ToString("O", CultureInfo.InvariantCulture),
                Number(sessionClock.Elapsed.TotalSeconds), lastBlock + 1,
                Number(d?.PowerWatts), Number(d?.CadenceRpm), Number(d?.SpeedKph),
                latestHeartRate?.ToString(CultureInfo.InvariantCulture) ?? "", eventName, workoutSnapshotFile);
            await recording.WriteLineAsync(line);
            await recording.FlushAsync(token);
        }
        finally { recordingGate.Release(); }
    }

    private async Task StopSessionAsync(string message)
    {
        ++sessionGeneration;
        sessionClock.Pause();
        sessionCancellation?.Cancel();
        bool hadRecording = recording != null;
        try { await RecordAsync("stopped", CancellationToken.None); }
        catch (Exception ex) { message += $" Recording error: {ex.Message}"; }
        sessionCancellation?.Dispose();
        sessionCancellation = null;
        await recordingGate.WaitAsync();
        try
        {
            if (recording != null) await recording.DisposeAsync();
            recording = null;
        }
        catch (Exception ex) { message += $" Recording could not be closed: {ex.Message}"; }
        finally { recordingGate.Release(); }
        sessionClock.Stop();
        runningWorkout = null;
        sessionStarting = false;
        sessionCommandPending = false;
        lastBlock = -1;
        if (!destroyed && sessionStatus != null)
        {
            sessionStatus.Text = message + (hadRecording ? " Ride saved on this phone." : "");
            RefreshSessionDisplay();
        }
    }

}
