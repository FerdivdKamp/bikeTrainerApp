using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using ErgTrainer.Sessions;

namespace ErgTrainer.Controls;

public sealed class WorkoutProgressControl : Control
{
    private TrainingSession? _session;
    private SessionPosition _position;
    private int _previousBlock = -1;
    private readonly Stopwatch _jump = new();
    private readonly Timer _animation = new() { Interval = 33 };
    private bool _running;
    private const int LeftMargin = 72;
    private const int BaseY = 195;

    public WorkoutProgressControl()
    {
        DoubleBuffered = true;
        Height = 230;
        BackColor = Color.FromArgb(241, 248, 252);
        _animation.Tick += (_, _) =>
        {
            Invalidate();
            if (_jump.ElapsedMilliseconds > 650) _animation.Stop();
        };
    }

    public void SetSession(TrainingSession? session)
    {
        _session = session;
        _position = default;
        _previousBlock = -1;
        _jump.Reset();
        _animation.Stop();
        Width = Math.Max(1200, LeftMargin + 100 + TotalWidth());
        Invalidate();
    }

    public int RiderX
    {
        get
        {
            if (_session == null || _position.BlockIndex < 0) return LeftMargin;
            int x = LeftMargin;
            for (int i = 0; i < _position.BlockIndex; i++) x += BlockWidth(_session.Blocks[i]);
            var block = _session.Blocks[_position.BlockIndex];
            return x + (int)(BlockWidth(block) * Math.Clamp(_position.BlockElapsedSeconds / block.DurationSeconds, 0, 1));
        }
    }

    public void SetProgress(SessionPosition position, bool running)
    {
        if (_session == null) return;
        if (_previousBlock >= 0 && position.BlockIndex != _previousBlock && running)
        {
            _jump.Restart();
            _animation.Start();
        }
        if (!running)
        {
            _jump.Stop();
            _animation.Stop();
        }
        else if (!_jump.IsRunning && _jump.Elapsed > TimeSpan.Zero && _jump.ElapsedMilliseconds < 600)
        {
            _jump.Start();
            _animation.Start();
        }
        _previousBlock = position.BlockIndex;
        _position = position;
        _running = running;
        Invalidate();
    }

    private int TotalWidth()
    {
        int width = 0;
        if (_session != null) foreach (var block in _session.Blocks) width += BlockWidth(block);
        return width;
    }

    private static int BlockWidth(TrainingBlock block) => Math.Max(56, (int)(block.DurationSeconds * 0.55));

    private static Color CategoryColor(string? category) => category?.ToLowerInvariant() switch
    {
        "warmup" => Color.FromArgb(81, 170, 129),
        "endurance" => Color.FromArgb(67, 159, 193),
        "tempo" => Color.FromArgb(66, 111, 202),
        "climb" => Color.FromArgb(137, 105, 195),
        "sprint" => Color.FromArgb(231, 99, 88),
        "recovery" => Color.FromArgb(99, 181, 181),
        "cooldown" => Color.FromArgb(136, 160, 179),
        _ => Color.FromArgb(206, 153, 93)
    };

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.SmoothingMode = SmoothingMode.AntiAlias;
        if (_session?.Blocks is not { Count: > 0 })
        {
            g.DrawString("Add blocks to preview the workout", Font, Brushes.DimGray, 20, 30);
            return;
        }

        double min = 0, max = 1;
        foreach (var block in _session.Blocks)
        {
            double value = _session.Mode == "erg" ? block.TargetWatts ?? 0 : block.GradientPercent ?? 0;
            min = Math.Min(min, value);
            max = Math.Max(max, value);
        }
        if (max - min < 1) max = min + 1;
        string unit = _session.Mode == "erg" ? "W" : "% grade";
        g.DrawString($"{max:0.#} {unit}", Font, Brushes.DarkSlateGray, 5, 25);
        g.DrawString($"{min:0.#} {unit}", Font, Brushes.DarkSlateGray, 5, BaseY - 8);
        g.DrawLine(Pens.LightSteelBlue, LeftMargin - 4, 35, LeftMargin - 4, BaseY + 2);
        g.DrawLine(Pens.LightSteelBlue, LeftMargin, BaseY, Width, BaseY);

        int x = LeftMargin;
        int riderY = BaseY - 10;
        int oldY = riderY;
        for (int i = 0; i < _session.Blocks.Count; i++)
        {
            var block = _session.Blocks[i];
            int width = BlockWidth(block);
            double value = _session.Mode == "erg" ? block.TargetWatts ?? 0 : block.GradientPercent ?? 0;
            int top = BaseY - 22 - (int)((value - min) / (max - min) * 115);
            var color = CategoryColor(block.Category);
            if (_position.BlockIndex > i) color = Color.FromArgb(155, 171, 177);
            using var brush = new SolidBrush(color);
            g.FillRectangle(brush, x + 2, top, width - 4, BaseY - top);
            if (_position.BlockIndex == i)
            {
                using var outline = new Pen(Color.FromArgb(27, 45, 70), 3);
                g.DrawRectangle(outline, x + 2, top, width - 4, BaseY - top);
                riderY = top - 12;
                if (i > 0)
                {
                    double previous = _session.Mode == "erg" ? _session.Blocks[i - 1].TargetWatts ?? 0 : _session.Blocks[i - 1].GradientPercent ?? 0;
                    oldY = BaseY - 34 - (int)((previous - min) / (max - min) * 115);
                }
            }
            g.SetClip(new Rectangle(x + 5, top + 5, width - 10, 35));
            g.DrawString(block.Name, Font, Brushes.White, x + 6, top + 5);
            g.ResetClip();
            x += width;
        }

        if (_position.BlockIndex >= 0 && !_position.Finished)
        {
            int px = RiderX;
            int py = riderY;
            if (_jump.IsRunning && _running)
            {
                double t = Math.Clamp(_jump.Elapsed.TotalSeconds / 0.6, 0, 1);
                py = (int)(oldY + (riderY - oldY) * t - 25 * Math.Sin(Math.PI * t));
            }
            // An original, simple cyclist drawn from shapes; no external sprite assets.
            using var ink = new Pen(Color.FromArgb(32, 50, 71), 2);
            g.DrawEllipse(ink, px - 13, py + 4, 10, 10);
            g.DrawEllipse(ink, px + 5, py + 4, 10, 10);
            g.DrawLine(ink, px - 8, py + 9, px + 2, py - 1);
            g.DrawLine(ink, px + 2, py - 1, px + 10, py + 9);
            g.DrawLine(ink, px - 8, py + 9, px + 10, py + 9);
            g.DrawLine(ink, px + 2, py - 1, px - 1, py - 12);
            g.FillEllipse(Brushes.DarkOrange, px - 5, py - 19, 9, 9);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) _animation.Dispose();
        base.Dispose(disposing);
    }
}
