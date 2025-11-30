using System;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace ErgTrainer.Controls
{
    public class TrainingTimerControl : UserControl
    {
        private readonly Button _btnStart;
        private readonly Button _btnPause;
        private readonly Button _btnStop;
        private readonly Label _lblTimer;
        private readonly System.Windows.Forms.Timer _timerUpdate;
        private readonly Stopwatch _stopwatch;
        private TimeSpan _pausedTime = TimeSpan.Zero;
        private bool _isPaused = false;

        public event EventHandler? Started;
        public event EventHandler? Paused;
        public event EventHandler? Stopped;

        public bool IsRunning => _stopwatch.IsRunning && !_isPaused;

        public TrainingTimerControl()
        {
            _stopwatch = new Stopwatch();
            _timerUpdate = new System.Windows.Forms.Timer
            {
                Interval = 100 // Update every 100ms
            };
            _timerUpdate.Tick += TimerUpdate_Tick;

            // Create UI
            _btnStart = new Button
            {
                Text = "Start",
                Left = 10,
                Top = 10,
                Width = 90,
                Height = 30
            };

            _btnPause = new Button
            {
                Text = "Pause",
                Left = 110,
                Top = 10,
                Width = 90,
                Height = 30,
                Enabled = false
            };

            _btnStop = new Button
            {
                Text = "Stop",
                Left = 210,
                Top = 10,
                Width = 90,
                Height = 30,
                Enabled = false
            };

            _lblTimer = new Label
            {
                Text = "00:00:00",
                Left = 10,
                Top = 50,
                Width = 290,
                Height = 40,
                Font = new Font("Arial", 24, FontStyle.Bold),
                TextAlign = ContentAlignment.MiddleCenter
            };

            _btnStart.Click += BtnStart_Click;
            _btnPause.Click += BtnPause_Click;
            _btnStop.Click += BtnStop_Click;

            Controls.AddRange(new Control[]
            {
                _btnStart,
                _btnPause,
                _btnStop,
                _lblTimer
            });

            Width = 310;
            Height = 100;
        }

        private void BtnStart_Click(object? sender, EventArgs e)
        {
            if (!_stopwatch.IsRunning)
            {
                // Start or resume the stopwatch
                if (_isPaused)
                {
                    // Resume from paused state
                    _stopwatch.Start();
                    _isPaused = false;
                }
                else
                {
                    // Start fresh
                    _stopwatch.Restart();
                    _pausedTime = TimeSpan.Zero;
                }
                
                _timerUpdate.Start();
            }
            
            // Update button states
            _btnStart.Enabled = false;
            _btnPause.Enabled = true;
            _btnStop.Enabled = true;

            Started?.Invoke(this, EventArgs.Empty);
        }

        private void BtnPause_Click(object? sender, EventArgs e)
        {
            if (_stopwatch.IsRunning)
            {
                _stopwatch.Stop();
                _pausedTime = _stopwatch.Elapsed;
                _isPaused = true;
                _timerUpdate.Stop();
                
                // Update button states
                _btnStart.Enabled = true;
                _btnPause.Enabled = false;
                _btnStop.Enabled = true;

                Paused?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BtnStop_Click(object? sender, EventArgs e)
        {
            _stopwatch.Stop();
            _stopwatch.Reset();
            _pausedTime = TimeSpan.Zero;
            _isPaused = false;
            _timerUpdate.Stop();
            
            // Update display
            _lblTimer.Text = "00:00:00";
            
            // Update button states
            _btnStart.Enabled = true;
            _btnPause.Enabled = false;
            _btnStop.Enabled = false;

            Stopped?.Invoke(this, EventArgs.Empty);
        }

        private void TimerUpdate_Tick(object? sender, EventArgs e)
        {
            if (_stopwatch.IsRunning && !_isPaused)
            {
                var elapsed = _stopwatch.Elapsed;
                _lblTimer.Text = $"{elapsed.Hours:D2}:{elapsed.Minutes:D2}:{elapsed.Seconds:D2}.{elapsed.Milliseconds / 100:D1}";
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _timerUpdate?.Stop();
                _timerUpdate?.Dispose();
                _stopwatch?.Stop();
            }
            base.Dispose(disposing);
        }
    }
}

