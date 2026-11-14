using System.Globalization;
using Android.Content;
using Android.Text;
using Android.Views;
using Android.Widget;
using ErgTrainer.Sessions;

namespace ErgTrainer.AndroidApp;

public partial class MainActivity
{
    private LinearLayout content = null!;
    private LinearLayout? connectPage, testPage, createPage, sessionPage;
    private EditText workoutName = null!, blockName = null!, blockSeconds = null!, blockWatts = null!, blockInstruction = null!;
    private LinearLayout blockList = null!;
    private TextView workoutStatus = null!, sessionStatus = null!, sessionDisplay = null!;
    private Button pauseButton = null!, startButton = null!, stopButton = null!;
    private TrainingSession workout = new();
    private int selectedBlock = -1;
    private string? selectedWorkoutPath;

    private LinearLayout Page()
    {
        return new LinearLayout(this) { Orientation = global::Android.Widget.Orientation.Vertical };
    }

    private TextView Label(string text, float size = 16) => new(this) { Text = text, TextSize = size };
    private EditText Input(string hint, string initial = "", bool numeric = false)
    {
        var input = new EditText(this) { Hint = hint, Text = initial };
        input.SetSingleLine(true);
        if (numeric) input.InputType = InputTypes.ClassNumber;
        return input;
    }

    private void ShowPage(string page)
    {
        if (destroyed) return;
        content.RemoveAllViews();
        var view = page switch
        {
            "Connect" => connectPage ??= BuildConnectPage(),
            "Test" => testPage ??= BuildTestPage(),
            "Create" => createPage ??= BuildCreatePage(),
            _ => sessionPage ??= BuildSessionPage()
        };
        content.AddView(view);
        if (page == "Session") RefreshSessionDisplay();
    }

    private LinearLayout BuildConnectPage()
    {
        var page = Page();
        page.AddView(Label("Connect devices", 24));
        page.AddView(status);
        page.AddView(scan);
        page.AddView(connectTrainer);
        page.AddView(connectHeartRate);
        page.AddView(MakeButton("Disconnect trainer", async () => await DisconnectTrainerAsync()));
        page.AddView(MakeButton("Disconnect heart-rate monitor", async () => await DisconnectHeartRateAsync()));
        page.AddView(readings);
        return page;
    }

    private LinearLayout BuildTestPage()
    {
        var page = Page();
        page.AddView(Label("Tests", 24));
        page.AddView(Label("Play the countdown with your music running."));
        page.AddView(sound);
        var target = Input("Target watts", "150", true);
        var result = Label("Connect a Tacx FE-C trainer to test ERG control.");
        page.AddView(Label("ERG control test"));
        page.AddView(target);
        page.AddView(MakeButton("Set test target", async () =>
        {
            if (sessionClock.IsActive || sessionStarting || sessionCommandPending)
            { result.Text = "Stop the session before testing trainer control."; return; }
            if (connectedTrainer == null || trainer.ControlMethod != "Tacx FE-C over BLE")
            { result.Text = "Connect a trainer with Tacx FE-C over BLE control first."; return; }
            if (!int.TryParse(target.Text, out int watts) || watts < 0 || watts > 1000)
            { result.Text = "Enter a target from 0 to 1000 W."; return; }
            sessionCommandPending = true;
            try
            {
                result.Text = "Setting target…";
                var response = await trainer.SetTargetPowerWithResultAsync(watts, lifetime.Token);
                if (!destroyed) result.Text = response.Message;
            }
            catch (System.OperationCanceledException) { }
            catch (Exception ex) { if (!destroyed) result.Text = $"Control test failed: {ex.Message}"; }
            finally { sessionCommandPending = false; }
        }));
        page.AddView(result);
        return page;
    }

    private LinearLayout BuildCreatePage()
    {
        var page = Page();
        page.AddView(Label("Create an ERG workout", 24));
        page.AddView(Label("Tap a block to edit; hold and drag to reorder. Duration accepts seconds or 60*5."));
        workoutName = Input("Workout name", workout.Name);
        blockName = Input("Block name", "Block");
        blockSeconds = Input("Duration in seconds (e.g. 60*5)", "60");
        blockWatts = Input("Target power (watts)", "150", true);
        blockInstruction = Input("Instruction (optional)");
        blockList = Page();
        workoutStatus = Label("");
        page.AddView(workoutName);
        page.AddView(blockList);
        page.AddView(MakeButton("Select block", SelectBlock));
        page.AddView(blockName);
        page.AddView(blockSeconds);
        page.AddView(blockWatts);
        page.AddView(blockInstruction);
        page.AddView(MakeButton("Add block", () => ChangeBlock(false)));
        page.AddView(MakeButton("Update selected block", () => ChangeBlock(true)));
        page.AddView(MakeButton("Duplicate selected block", DuplicateBlock));
        page.AddView(MakeButton("Move selected block up", () => MoveBlock(-1)));
        page.AddView(MakeButton("Move selected block down", () => MoveBlock(1)));
        page.AddView(MakeButton("Remove selected block", RemoveBlock));
        page.AddView(MakeButton("New workout", NewWorkout));
        page.AddView(MakeButton("Save workout", async () => await SaveWorkoutAsync()));
        page.AddView(MakeButton("Export workout to Drive or files", async () =>
        {
            string? previous = selectedWorkoutPath;
            await SaveWorkoutAsync();
            if (selectedWorkoutPath != null && selectedWorkoutPath != previous)
                ExportFile(selectedWorkoutPath, "application/json", "workout.ergsession.json");
        }));
        page.AddView(MakeButton("Open saved workout", async () => await OpenWorkoutAsync()));
        page.AddView(workoutStatus);
        return page;
    }

    private LinearLayout BuildSessionPage()
    {
        var page = Page();
        page.AddView(Label("Session", 24));
        sessionDisplay = Label("", 19);
        sessionStatus = Label("Select or create a workout, then connect your trainer.");
        startButton = MakeButton("Start session", async () => await StartSessionAsync());
        pauseButton = MakeButton("Pause / resume", PauseOrResume);
        stopButton = MakeButton("Stop and save", async () => await StopSessionAsync("Session stopped."));
        page.AddView(sessionDisplay);
        page.AddView(sessionStatus);
        page.AddView(startButton);
        page.AddView(pauseButton);
        page.AddView(stopButton);
        page.AddView(MakeButton("Open saved workout", async () => await OpenWorkoutAsync()));
        page.AddView(MakeButton("Saved ride recordings", ShowRecordings));
        page.AddView(MakeButton("Export saved ride", ExportRide));
        return page;
    }

    private void RefreshBlocks()
    {
        if (blockList == null) return;
        blockList.RemoveAllViews();
        if (workout.Blocks.Count == 0) { blockList.AddView(Label("No blocks yet.")); return; }
        for (int i = 0; i < workout.Blocks.Count; i++)
        {
            int index = i;
            var b = workout.Blocks[i];
            var row = Label($"{(i == selectedBlock ? "▶ " : "☰ ")}{i + 1}. {b.Name} — {b.DurationSeconds}s, {b.TargetWatts} W");
            row.SetPadding(12, 20, 12, 20);
            row.Click += (_, _) => LoadBlock(index);
            row.LongClick += (_, e) =>
            {
                if (sessionClock.IsActive || sessionStarting) { e.Handled = true; return; }
                row.StartDragAndDrop(ClipData.NewPlainText("block", index.ToString(CultureInfo.InvariantCulture)),
                    new View.DragShadowBuilder(row), null, 0);
                e.Handled = true;
            };
            row.Drag += (_, e) =>
            {
                if (e.Event?.Action == DragAction.Started) { e.Handled = true; return; }
                if (e.Event?.Action == DragAction.Drop)
                {
                    var text = e.Event.ClipData?.GetItemAt(0)?.Text;
                    if (int.TryParse(text, out int from) && from >= 0 && from < workout.Blocks.Count &&
                        index < workout.Blocks.Count && !sessionClock.IsActive && !sessionStarting)
                    {
                        var block = workout.Blocks[from];
                        workout.Blocks.RemoveAt(from);
                        workout.Blocks.Insert(index, block);
                        selectedBlock = index;
                        RefreshBlocks();
                    }
                }
                e.Handled = true;
            };
            blockList.AddView(row);
        }
    }

    private void SelectBlock()
    {
        if (workout.Blocks.Count == 0) { workoutStatus.Text = "Add a block first."; return; }
        var names = workout.Blocks.Select((b, i) => $"{i + 1}. {b.Name}").ToArray();
        var dialog = new AlertDialog.Builder(this);
        dialog.SetTitle("Select a block");
        dialog.SetItems(names, (_, e) =>
        {
            LoadBlock(e.Which);
        });
        dialog.SetNegativeButton("Cancel", (_, _) => { });
        dialog.Show();
    }

    private void LoadBlock(int index)
    {
        selectedBlock = index;
        var b = workout.Blocks[index];
        blockName.Text = b.Name;
        blockSeconds.Text = b.DurationSeconds.ToString(CultureInfo.InvariantCulture);
        blockWatts.Text = b.TargetWatts?.ToString(CultureInfo.InvariantCulture) ?? "";
        blockInstruction.Text = b.Instruction;
        RefreshBlocks();
    }

    private void DuplicateBlock()
    {
        if (sessionClock.IsActive || sessionStarting) { workoutStatus.Text = "Stop the session before editing."; return; }
        if (selectedBlock < 0 || selectedBlock >= workout.Blocks.Count)
        { workoutStatus.Text = "Select a block first."; return; }
        workout.Blocks.Insert(selectedBlock + 1, workout.Blocks[selectedBlock].Copy());
        LoadBlock(selectedBlock + 1);
        workoutStatus.Text = "Block duplicated. Save the workout to keep it.";
    }

    private void ChangeBlock(bool update)
    {
        if (sessionClock.IsActive || sessionStarting) { workoutStatus.Text = "Stop the session before editing."; return; }
        if (update && (selectedBlock < 0 || selectedBlock >= workout.Blocks.Count))
        { workoutStatus.Text = "Select a block first."; return; }
        if (string.IsNullOrWhiteSpace(blockName.Text) ||
            !DurationExpression.TryParse(blockSeconds.Text, out int seconds) ||
            !int.TryParse(blockWatts.Text, out int watts) || watts < 0 || watts > 1000)
        { workoutStatus.Text = "Enter a name, a duration such as 60*5 (up to 86400 seconds), and 0–1000 watts."; return; }
        var block = new TrainingBlock { Name = blockName.Text.Trim(), DurationSeconds = seconds,
            TargetWatts = watts, Instruction = blockInstruction.Text ?? "" };
        if (update) workout.Blocks[selectedBlock] = block;
        else { workout.Blocks.Add(block); selectedBlock = workout.Blocks.Count - 1; }
        workoutStatus.Text = "Block updated. Save the workout to keep it.";
        RefreshBlocks();
    }

    private void MoveBlock(int direction)
    {
        if (sessionClock.IsActive || sessionStarting) return;
        int next = selectedBlock + direction;
        if (selectedBlock < 0 || next < 0 || next >= workout.Blocks.Count) return;
        (workout.Blocks[selectedBlock], workout.Blocks[next]) = (workout.Blocks[next], workout.Blocks[selectedBlock]);
        selectedBlock = next;
        RefreshBlocks();
    }

    private void RemoveBlock()
    {
        if (sessionClock.IsActive || sessionStarting || selectedBlock < 0) return;
        workout.Blocks.RemoveAt(selectedBlock);
        selectedBlock = -1;
        RefreshBlocks();
    }

    private void NewWorkout()
    {
        if (sessionClock.IsActive || sessionStarting) { workoutStatus.Text = "Stop the session before editing."; return; }
        workout = new TrainingSession();
        selectedWorkoutPath = null;
        selectedBlock = -1;
        workoutName.Text = workout.Name;
        workoutStatus.Text = "New workout. Existing saved workouts are kept.";
        RefreshBlocks();
    }

    private TrainingSession SnapshotWorkout()
    {
        if (workoutName != null) workout.Name = workoutName.Text?.Trim() ?? "";
        return new TrainingSession { Name = workout.Name, Description = workout.Description, Mode = workout.Mode,
            RiderWeightKg = workout.RiderWeightKg, BikeWeightKg = workout.BikeWeightKg,
            Blocks = workout.Blocks.Select(b => b.Copy()).ToList() };
    }

    private string WorkoutDirectory => Path.Combine(FilesDir!.AbsolutePath, "Workouts");
    private string RecordingDirectory => Path.Combine(FilesDir!.AbsolutePath, "Rides");

    private async Task SaveWorkoutAsync()
    {
        try
        {
            var snapshot = SnapshotWorkout();
            var errors = snapshot.Validate();
            if (errors.Count > 0) { workoutStatus.Text = string.Join("\n", errors); return; }
            Directory.CreateDirectory(WorkoutDirectory);
            // Every save is a new file; no earlier workout is overwritten.
            string path = Path.Combine(WorkoutDirectory, Guid.NewGuid().ToString("N") + ".ergsession.json");
            await TrainingSessionFiles.SaveAsync(path, snapshot, lifetime.Token);
            selectedWorkoutPath = path;
            workoutStatus.Text = "Workout saved on this phone.";
        }
        catch (Exception ex) when (ex is not System.OperationCanceledException)
        { workoutStatus.Text = $"Could not save workout: {ex.Message}"; }
    }

    private async Task OpenWorkoutAsync()
    {
        try
        {
            Directory.CreateDirectory(WorkoutDirectory);
            var paths = Directory.GetFiles(WorkoutDirectory, "*.ergsession.json");
            var items = new List<(string Path, TrainingSession Session)>();
            foreach (var path in paths)
            {
                try { items.Add((path, await TrainingSessionFiles.LoadAsync(path, lifetime.Token))); }
                catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
                { System.Diagnostics.Debug.WriteLine(ex); }
            }
            if (items.Count == 0) { Toast.MakeText(this, "No saved workouts yet.", ToastLength.Long)?.Show(); return; }
            var dialog = new AlertDialog.Builder(this);
            dialog.SetTitle("Open saved workout");
            dialog.SetItems(items.Select(x => $"{x.Session.Name} — {File.GetLastWriteTime(x.Path):g}").ToArray(), (_, e) =>
            {
                if (sessionClock.IsActive || sessionStarting)
                { Toast.MakeText(this, "Stop the session first.", ToastLength.Short)?.Show(); return; }
                workout = items[e.Which].Session;
                selectedWorkoutPath = items[e.Which].Path;
                selectedBlock = -1;
                if (workoutName != null) workoutName.Text = workout.Name;
                RefreshBlocks();
                RefreshSessionDisplay();
            });
            dialog.SetNegativeButton("Cancel", (_, _) => { });
            dialog.Show();
        }
        catch (Exception ex) when (ex is not System.OperationCanceledException)
        { Toast.MakeText(this, $"Could not open workouts: {ex.Message}", ToastLength.Long)?.Show(); }
    }

    private void ShowRecordings()
    {
        Directory.CreateDirectory(RecordingDirectory);
        var files = Directory.GetFiles(RecordingDirectory, "*.csv")
            .OrderByDescending(File.GetLastWriteTimeUtc).Select(Path.GetFileName).ToArray();
        var dialog = new AlertDialog.Builder(this);
        dialog.SetTitle("Saved rides on this phone");
        dialog.SetMessage(files.Length == 0 ? "No rides saved yet." : string.Join("\n", files));
        dialog.SetPositiveButton("OK", (_, _) => { });
        dialog.Show();
    }

    private string? pendingExport;
    private const int ExportRequest = 42;

    private void ExportRide()
    {
        Directory.CreateDirectory(RecordingDirectory);
        var files = Directory.GetFiles(RecordingDirectory, "*.csv")
            .OrderByDescending(File.GetLastWriteTimeUtc).ToArray();
        if (files.Length == 0) { Toast.MakeText(this, "No rides saved yet.", ToastLength.Long)?.Show(); return; }
        var dialog = new AlertDialog.Builder(this);
        dialog.SetTitle("Export ride");
        dialog.SetItems(files.Select(f => Path.GetFileName(f) ?? f).ToArray(), (_, e) =>
            ExportFile(files[e.Which], "text/csv", Path.GetFileName(files[e.Which])));
        dialog.SetNegativeButton("Cancel", (_, _) => { });
        dialog.Show();
    }

    private void ExportFile(string path, string mime, string suggestedName)
    {
        pendingExport = path;
        var intent = new Intent(Intent.ActionCreateDocument);
        intent.AddCategory(Intent.CategoryOpenable);
        intent.SetType(mime);
        intent.PutExtra(Intent.ExtraTitle, suggestedName);
        try { StartActivityForResult(intent, ExportRequest); }
        catch (Exception ex)
        {
            pendingExport = null;
            Toast.MakeText(this, $"Could not open file picker: {ex.Message}", ToastLength.Long)?.Show();
        }
    }

    protected override void OnSaveInstanceState(global::Android.OS.Bundle outState)
    {
        outState.PutString("pendingExport", pendingExport);
        base.OnSaveInstanceState(outState);
    }

    protected override async void OnActivityResult(int requestCode, Result resultCode, Intent? data)
    {
        base.OnActivityResult(requestCode, resultCode, data);
        if (requestCode != ExportRequest) return;
        string? path = pendingExport;
        pendingExport = null;
        if (resultCode != Result.Ok || data?.Data == null || path == null) return;
        try
        {
            await using var input = File.OpenRead(path);
            await using var output = ContentResolver?.OpenOutputStream(data.Data)
                ?? throw new IOException("The selected destination could not be opened.");
            await input.CopyToAsync(output);
            await output.FlushAsync();
            Toast.MakeText(this, "Export saved.", ToastLength.Long)?.Show();
        }
        catch (Exception ex)
        { Toast.MakeText(this, $"Export failed: {ex.Message}", ToastLength.Long)?.Show(); }
    }
}
