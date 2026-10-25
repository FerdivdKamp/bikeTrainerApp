using System;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using ErgTrainer.Sessions;

namespace ErgTrainer.Controls;

public sealed class SessionBuilderControl : UserControl
{
    private readonly Panel _content = new() { Width = 1290, Height = 980 };
    private readonly TextBox _sessionName = new() { Left = 100, Top = 12, Width = 390, Text = "New session" };
    private readonly TextBox _description = new() { Left = 100, Top = 45, Width = 810 };
    private readonly ComboBox _mode = new() { Left = 565, Top = 12, Width = 170, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Label _riderWeightLabel = Label("Rider kg:", 925, 15, 95);
    private readonly NumericUpDown _riderWeight = new() { Left = 1025, Top = 12, Width = 110, Minimum = .01m, Maximum = 655.34m, DecimalPlaces = 2, Increment = .5m, Value = 75 };
    private readonly Label _bikeWeightLabel = Label("Bike kg:", 925, 48, 95);
    private readonly NumericUpDown _bikeWeight = new() { Left = 1025, Top = 45, Width = 110, Minimum = .01m, Maximum = 50, DecimalPlaces = 2, Increment = .5m, Value = 10 };
    private readonly ListBox _blocks = new() { Left = 10, Top = 154, Width = 460, Height = 320 };
    private readonly TextBox _blockName = new() { Left = 590, Top = 164, Width = 330, Text = "Block" };
    private readonly NumericUpDown _duration = new() { Left = 590, Top = 197, Width = 120, Minimum = 1, Maximum = 86400, Value = 60 };
    private readonly NumericUpDown _watts = new() { Left = 590, Top = 230, Width = 120, Minimum = 0, Maximum = 1000, Value = 150 };
    private readonly NumericUpDown _gradient = new() { Left = 590, Top = 230, Width = 120, Minimum = -20, Maximum = 20, DecimalPlaces = 1, Increment = .5m };
    private readonly Label _targetLabel = new() { Left = 490, Top = 234, Width = 90 };
    private readonly ComboBox _category = new() { Left = 590, Top = 263, Width = 180, DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly TextBox _instruction = new() { Left = 590, Top = 298, Width = 550, Height = 105, Multiline = true, ScrollBars = ScrollBars.Vertical };
    private readonly Label _total = new() { Left = 10, Top = 485, Width = 350, Height = 25 };
    private readonly Label _status = new() { Left = 10, Top = 940, Width = 1200, Height = 25, AutoEllipsis = true };
    private readonly TextBox _current = new() { Left = 10, Top = 760, Width = 1250, Height = 78, Font = new Font("Segoe UI", 16, FontStyle.Bold), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
    private readonly TextBox _next = new() { Left = 10, Top = 841, Width = 1250, Height = 52, Font = new Font("Segoe UI", 11), Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.White };
    private readonly ProgressBar _overall = new() { Left = 10, Top = 902, Width = 980, Height = 24, Maximum = 1000 };
    private readonly Button _start = new() { Text = "Start session", Left = 1000, Top = 898, Width = 120 };
    private readonly Button _pause = new() { Text = "Pause", Left = 1125, Top = 898, Width = 70, Enabled = false };
    private readonly Button _stop = new() { Text = "Stop", Left = 1200, Top = 898, Width = 65, Enabled = false };
    private readonly Panel _previewScroll = new() { Left = 10, Top = 516, Width = 1250, Height = 235, AutoScroll = true, BorderStyle = BorderStyle.FixedSingle };
    private readonly WorkoutProgressControl _preview = new() { Left = 0, Top = 0 };
    private readonly Panel _editing = new() { Left = 0, Top = 0, Width = 1270, Height = 512 };
    private TrainingSession _session = new();
    private bool _loading;
    private bool _active;

    public event Action<TrainingSession>? StartRequested;
    public event Action? PauseRequested;
    public event Action? ResumeRequested;
    public event Action? StopRequested;

    public SessionBuilderControl()
    {
        Dock = DockStyle.Fill;
        AutoScroll = true;
        Controls.Add(_content);
        _content.Controls.Add(_editing);
        _mode.Items.AddRange(new object[] { "ERG", "Simulation" });
        _category.Items.AddRange(new object[] { "Warm-up", "Endurance", "Tempo", "Climb", "Sprint", "Recovery", "Cooldown", "Custom" });
        _category.SelectedIndex = 7;
        _mode.SelectedIndex = 0;

        _editing.Controls.AddRange(new Control[]
        {
            Label("Session:", 10, 15, 90), _sessionName, Label("Mode:", 510, 15, 55), _mode,
            Label("Description:", 10, 48, 90), _description,
            _riderWeightLabel, _riderWeight, _bikeWeightLabel, _bikeWeight,
            Label("Blocks (ordered)", 10, 130, 250), _blocks,
            Label("Name:", 490, 167, 90), _blockName,
            Label("Seconds:", 490, 200, 90), _duration,
            _targetLabel, _watts, _gradient,
            Label("Category:", 490, 266, 90), _category,
            Label("Instruction:", 490, 300, 100), _instruction,
            _total
        });
        int x = 10;
        foreach (var (caption, action, width) in new (string, Action, int)[]
        {
            ("New", NewSession, 68), ("Load", LoadSession, 68), ("Save", SaveSession, 68),
            ("Add", AddBlock, 68), ("Update", UpdateBlock, 72), ("Duplicate", DuplicateBlock, 85),
            ("Remove", RemoveBlock, 75), ("Up", () => MoveBlock(-1), 55), ("Down", () => MoveBlock(1), 62)
        })
        {
            var button = new Button { Text = caption, Left = x, Top = 95, Width = width, Height = 29 };
            button.Click += (_, _) => action();
            _editing.Controls.Add(button);
            x += width + 5;
        }

        _previewScroll.Controls.Add(_preview);
        _content.Controls.AddRange(new Control[] { _previewScroll, _current, _next, _overall, _start, _pause, _stop, _status });
        _mode.SelectedIndexChanged += (_, _) => ModeChanged();
        _blocks.SelectedIndexChanged += (_, _) => LoadSelectedBlock();
        _sessionName.TextChanged += (_, _) => RefreshPreview();
        _description.TextChanged += (_, _) => RefreshPreview();
        _start.Click += (_, _) =>
        {
            var session = Snapshot();
            var errors = session.Validate();
            if (errors.Count != 0) { ShowErrors(errors); return; }
            StartRequested?.Invoke(session);
        };
        _pause.Click += (_, _) =>
        {
            if (_pause.Text == "Pause") PauseRequested?.Invoke();
            else ResumeRequested?.Invoke();
        };
        _stop.Click += (_, _) => StopRequested?.Invoke();
        RefreshPreview();
    }

    private static Label Label(string text, int left, int top, int width) =>
        new() { Text = text, Left = left, Top = top, Width = width, Height = 25 };

    private TrainingSession Snapshot() => new()
    {
        Name = _sessionName.Text, Description = _description.Text,
        Mode = _mode.SelectedIndex == 0 ? "erg" : "simulation",
        RiderWeightKg = (double)_riderWeight.Value, BikeWeightKg = (double)_bikeWeight.Value,
        Blocks = _session.Blocks.Select(b => b.Copy()).ToList()
    };

    private void RefreshPreview()
    {
        if (_loading) return;
        var snapshot = Snapshot();
        _preview.SetSession(snapshot);
        long total = snapshot.TotalSeconds;
        _total.Text = $"Total duration: {total / 3600:00}:{total / 60 % 60:00}:{total % 60:00} ({snapshot.Blocks.Count} blocks)";
        _targetLabel.Text = snapshot.Mode == "erg" ? "Watts:" : "Gradient %:";
        _watts.Visible = snapshot.Mode == "erg";
        _gradient.Visible = snapshot.Mode == "simulation";
        _riderWeightLabel.Visible = _riderWeight.Visible = snapshot.Mode == "simulation";
        _bikeWeightLabel.Visible = _bikeWeight.Visible = snapshot.Mode == "simulation";
        _start.Enabled = !_active && snapshot.Mode == "erg";
        if (!_active)
            _status.Text = snapshot.Mode == "simulation"
                ? "Simulation editing and preview are available. Hardware start is disabled until gradient blocks are wired to trainer control."
                : "ERG session ready to validate and start after connecting a Tacx FE-C-over-BLE trainer.";
    }

    private void RefreshBlocks(int selected = -1)
    {
        _blocks.Items.Clear();
        foreach (var block in _session.Blocks)
            _blocks.Items.Add($"{block.Name}  |  {block.DurationSeconds / 3600:00}:{block.DurationSeconds / 60 % 60:00}:{block.DurationSeconds % 60:00}  |  " +
                (_mode.SelectedIndex == 0 ? $"{block.TargetWatts} W" : $"{block.GradientPercent:0.#}%"));
        if (selected >= 0 && selected < _blocks.Items.Count) _blocks.SelectedIndex = selected;
        RefreshPreview();
    }

    private void LoadSelectedBlock()
    {
        if (_blocks.SelectedIndex < 0 || _loading) return;
        var block = _session.Blocks[_blocks.SelectedIndex];
        _blockName.Text = block.Name;
        _duration.Value = Math.Clamp(block.DurationSeconds, 1, 86400);
        _watts.Value = Math.Clamp(block.TargetWatts ?? 150, 0, 1000);
        _gradient.Value = Math.Clamp((decimal)(block.GradientPercent ?? 0), -20, 20);
        _instruction.Text = block.Instruction;
        _category.SelectedItem = _category.Items.Cast<string>().FirstOrDefault(s => s.Equals(block.Category, StringComparison.OrdinalIgnoreCase)) ?? "Custom";
    }

    private TrainingBlock EditorBlock() => new()
    {
        Name = _blockName.Text.Trim(), DurationSeconds = (int)_duration.Value,
        TargetWatts = _mode.SelectedIndex == 0 ? (int)_watts.Value : null,
        GradientPercent = _mode.SelectedIndex == 1 ? (double)_gradient.Value : null,
        Category = (_category.SelectedItem?.ToString() ?? "Custom").ToLowerInvariant().Replace("-", ""),
        Instruction = _instruction.Text
    };

    private void AddBlock()
    {
        _session.Blocks.Add(EditorBlock());
        RefreshBlocks(_session.Blocks.Count - 1);
    }

    private void UpdateBlock()
    {
        int index = _blocks.SelectedIndex;
        if (index < 0) { _status.Text = "Select a block to update."; return; }
        _session.Blocks[index] = EditorBlock();
        RefreshBlocks(index);
    }

    private void DuplicateBlock()
    {
        int index = _blocks.SelectedIndex;
        if (index < 0) { _status.Text = "Select a block to duplicate."; return; }
        _session.Blocks.Insert(index + 1, _session.Blocks[index].Copy());
        RefreshBlocks(index + 1);
    }

    private void RemoveBlock()
    {
        int index = _blocks.SelectedIndex;
        if (index < 0) return;
        _session.Blocks.RemoveAt(index);
        RefreshBlocks(Math.Min(index, _session.Blocks.Count - 1));
    }

    private void MoveBlock(int direction)
    {
        int index = _blocks.SelectedIndex, next = index + direction;
        if (index < 0 || next < 0 || next >= _session.Blocks.Count) return;
        (_session.Blocks[index], _session.Blocks[next]) = (_session.Blocks[next], _session.Blocks[index]);
        RefreshBlocks(next);
    }

    private void ModeChanged()
    {
        if (_loading) return;
        if (_session.Blocks.Count > 0)
        {
            if (MessageBox.Show("Changing mode resets all block targets. Continue?", "Change session mode",
                    MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
            {
                _loading = true;
                _mode.SelectedIndex = _session.Mode == "erg" ? 0 : 1;
                _loading = false;
                return;
            }
            foreach (var block in _session.Blocks)
            {
                block.TargetWatts = _mode.SelectedIndex == 0 ? 150 : null;
                block.GradientPercent = _mode.SelectedIndex == 1 ? 0 : null;
            }
        }
        _session.Mode = _mode.SelectedIndex == 0 ? "erg" : "simulation";
        RefreshBlocks();
    }

    private void NewSession()
    {
        _session = new TrainingSession();
        _loading = true;
        _sessionName.Text = _session.Name;
        _description.Text = "";
        _riderWeight.Value = (decimal)_session.RiderWeightKg;
        _bikeWeight.Value = (decimal)_session.BikeWeightKg;
        _mode.SelectedIndex = 0;
        _loading = false;
        RefreshBlocks();
    }

    private async void LoadSession()
    {
        using var dialog = new OpenFileDialog { Filter = "ErgTrainer sessions (*.ergsession.json)|*.ergsession.json|JSON files (*.json)|*.json" };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            var session = await TrainingSessionFiles.LoadAsync(dialog.FileName);
            if (IsDisposed) return;
            _loading = true;
            _session = session;
            _sessionName.Text = session.Name;
            _description.Text = session.Description;
            _riderWeight.Value = (decimal)session.RiderWeightKg;
            _bikeWeight.Value = (decimal)session.BikeWeightKg;
            _mode.SelectedIndex = session.Mode == "erg" ? 0 : 1;
            _loading = false;
            RefreshBlocks();
            _status.Text = $"Loaded {dialog.FileName}";
        }
        catch (Exception ex)
        {
            _loading = false;
            MessageBox.Show(this, ex.Message, "Unable to load session", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private async void SaveSession()
    {
        var session = Snapshot();
        var errors = session.Validate();
        if (errors.Count != 0) { ShowErrors(errors); return; }
        using var dialog = new SaveFileDialog { Filter = "ErgTrainer sessions (*.ergsession.json)|*.ergsession.json", DefaultExt = "ergsession.json", AddExtension = true, OverwritePrompt = true };
        if (dialog.ShowDialog(this) != DialogResult.OK) return;
        try
        {
            await TrainingSessionFiles.SaveAsync(dialog.FileName, session);
            if (!IsDisposed) _status.Text = $"Saved {dialog.FileName}";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "Unable to save session", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ShowErrors(System.Collections.Generic.IReadOnlyList<string> errors) =>
        MessageBox.Show(this, string.Join(Environment.NewLine, errors), "Invalid session", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    public void SetRunState(bool active, bool running, string message)
    {
        _active = active;
        _editing.Enabled = !active;
        _pause.Enabled = active;
        _pause.Text = running ? "Pause" : "Resume";
        _stop.Enabled = active;
        _start.Enabled = !active && _mode.SelectedIndex == 0;
        if (!active) RefreshPreview();
        _status.Text = message;
    }

    public void SetStartPending() => _pause.Enabled = false;

    public void SetRecoveryState(string message)
    {
        SetRunState(true, false, message);
        _pause.Enabled = false;
    }

    public void ShowPosition(TrainingSession session, SessionPosition position, bool running)
    {
        if (position.BlockIndex < 0) return;
        var block = session.Blocks[position.BlockIndex];
        string target = session.Mode == "erg" ? $"{block.TargetWatts} W" : $"{block.GradientPercent:0.#}% grade";
        _current.Text = $"NOW: {block.Name} ({target}) — " +
            (string.IsNullOrWhiteSpace(block.Instruction) ? "Keep riding" : block.Instruction);
        var upcoming = position.BlockIndex + 1 < session.Blocks.Count ? session.Blocks[position.BlockIndex + 1] : null;
        _next.Text = position.BlockIndex + 1 < session.Blocks.Count
            ? $"UP NEXT: {upcoming!.Name} ({upcoming.DurationSeconds} s, " +
              (session.Mode == "erg" ? $"{upcoming.TargetWatts} W)" : $"{upcoming.GradientPercent:0.#}% grade)") +
              (string.IsNullOrWhiteSpace(upcoming.Instruction) ? "" : $" — {upcoming.Instruction}")
            : "UP NEXT: Session complete";
        double remaining = block.DurationSeconds - position.BlockElapsedSeconds;
        if (running && remaining <= 5 && remaining > 0 && position.BlockIndex + 1 < session.Blocks.Count)
            _next.Text = $"{Math.Ceiling(remaining):0}…  " + _next.Text;
        _overall.Value = (int)Math.Clamp(position.TotalElapsedSeconds / session.TotalSeconds * 1000, 0, 1000);
        _preview.SetProgress(position, running);
        if (running && _preview.Width > _previewScroll.ClientSize.Width)
            _previewScroll.AutoScrollPosition = new Point(Math.Max(0, _preview.RiderX - _previewScroll.ClientSize.Width / 2), 0);
    }
}
