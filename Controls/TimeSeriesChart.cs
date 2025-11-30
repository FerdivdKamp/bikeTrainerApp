using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Windows.Forms;

namespace ErgTrainer.Controls
{
    public class TimeSeriesChart : Control
    {
        private readonly List<DataPoint> _dataPoints = new List<DataPoint>();
        private readonly string _title;
        private readonly string _unit;
        private readonly Color _lineColor;
        private DateTime _startTime;
        private bool _isRecording = false;
        private const int TimeWindowSeconds = 120; // 2 minutes
        private const int MaxDataPoints = 1000; // Limit data points for performance

        public TimeSeriesChart(string title, string unit, Color lineColor)
        {
            _title = title;
            _unit = unit;
            _lineColor = lineColor;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.DoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        public void StartRecording()
        {
            _isRecording = true;
            _startTime = DateTime.Now;
            _dataPoints.Clear();
            Invalidate();
        }

        public void StopRecording()
        {
            _isRecording = false;
        }

        public bool IsRecording => _isRecording;

        public void AddDataPoint(double value, DateTime timestamp)
        {
            if (!_isRecording) return;

            _dataPoints.Add(new DataPoint { Value = value, Timestamp = timestamp });

            // Remove old data points outside the time window
            var cutoffTime = timestamp.AddSeconds(-TimeWindowSeconds);
            _dataPoints.RemoveAll(p => p.Timestamp < cutoffTime);

            // Limit total data points for performance
            if (_dataPoints.Count > MaxDataPoints)
            {
                _dataPoints.RemoveRange(0, _dataPoints.Count - MaxDataPoints);
            }

            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            var clientRect = ClientRectangle;
            var padding = 50;
            var chartArea = new Rectangle(
                padding,
                padding,
                clientRect.Width - padding * 2,
                clientRect.Height - padding * 2
            );

            // Draw background
            g.FillRectangle(Brushes.White, chartArea);

            // Draw border
            g.DrawRectangle(Pens.LightGray, chartArea);

            if (_dataPoints.Count == 0)
            {
                // Draw empty state
                var emptyText = "No data";
                var emptySize = g.MeasureString(emptyText, Font);
                g.DrawString(emptyText, Font, Brushes.Gray,
                    chartArea.Left + (chartArea.Width - emptySize.Width) / 2,
                    chartArea.Top + (chartArea.Height - emptySize.Height) / 2);
                return;
            }

            // Calculate time range (last 2 minutes or all data if less than 2 minutes)
            var now = DateTime.Now;
            var timeRange = TimeSpan.FromSeconds(TimeWindowSeconds);
            var minTime = _dataPoints.Min(p => p.Timestamp);
            var maxTime = _dataPoints.Max(p => p.Timestamp);
            var actualTimeRange = maxTime - minTime;
            var displayTimeRange = actualTimeRange > timeRange ? timeRange : actualTimeRange;
            var displayStartTime = maxTime - displayTimeRange;

            // Filter data points in display range
            var displayPoints = _dataPoints.Where(p => p.Timestamp >= displayStartTime).ToList();

            if (displayPoints.Count == 0) return;

            // Calculate value range
            var minValue = displayPoints.Min(p => p.Value);
            var maxValue = displayPoints.Max(p => p.Value);
            var valueRange = maxValue - minValue;
            if (valueRange == 0) valueRange = 1; // Avoid division by zero

            // Draw grid lines and labels
            DrawGrid(g, chartArea, displayStartTime, maxTime, minValue, maxValue);

            // Draw data line
            if (displayPoints.Count > 1)
            {
                using (var pen = new Pen(_lineColor, 2))
                {
                    for (int i = 0; i < displayPoints.Count - 1; i++)
                    {
                        var p1 = displayPoints[i];
                        var p2 = displayPoints[i + 1];

                        var x1 = chartArea.Left + (float)((p1.Timestamp - displayStartTime).TotalSeconds / displayTimeRange.TotalSeconds * chartArea.Width);
                        var y1 = chartArea.Bottom - (float)((p1.Value - minValue) / valueRange * chartArea.Height);

                        var x2 = chartArea.Left + (float)((p2.Timestamp - displayStartTime).TotalSeconds / displayTimeRange.TotalSeconds * chartArea.Width);
                        var y2 = chartArea.Bottom - (float)((p2.Value - minValue) / valueRange * chartArea.Height);

                        g.DrawLine(pen, x1, y1, x2, y2);
                    }
                }
            }

            // Draw title
            var titleFont = new Font(Font.FontFamily, 10, FontStyle.Bold);
            var titleText = $"{_title} ({_unit})";
            var titleSize = g.MeasureString(titleText, titleFont);
            g.DrawString(titleText, titleFont, Brushes.Black, chartArea.Left, 5);

            // Draw current value
            if (displayPoints.Count > 0)
            {
                var currentValue = displayPoints.Last().Value;
                var valueText = $"{currentValue:F1} {_unit}";
                var valueSize = g.MeasureString(valueText, Font);
                g.DrawString(valueText, Font, Brushes.Black, chartArea.Right - valueSize.Width, 5);
            }
        }

        private void DrawGrid(Graphics g, Rectangle chartArea, DateTime startTime, DateTime endTime, double minValue, double maxValue)
        {
            // Horizontal grid lines (value axis)
            int horizontalLines = 5;
            for (int i = 0; i <= horizontalLines; i++)
            {
                var value = minValue + (maxValue - minValue) * i / horizontalLines;
                var y = chartArea.Bottom - (float)(i / (double)horizontalLines * chartArea.Height);
                
                g.DrawLine(Pens.LightGray, chartArea.Left, y, chartArea.Right, y);
                
                var labelText = $"{value:F0}";
                var labelSize = g.MeasureString(labelText, Font);
                g.DrawString(labelText, Font, Brushes.Gray, chartArea.Left - labelSize.Width - 5, y - labelSize.Height / 2);
            }

            // Vertical grid lines (time axis)
            int verticalLines = 6;
            var timeRange = endTime - startTime;
            for (int i = 0; i <= verticalLines; i++)
            {
                var time = startTime.AddSeconds(timeRange.TotalSeconds * i / verticalLines);
                var x = chartArea.Left + (float)(i / (double)verticalLines * chartArea.Width);
                
                g.DrawLine(Pens.LightGray, x, chartArea.Top, x, chartArea.Bottom);
                
                var labelText = time.ToString("mm:ss");
                var labelSize = g.MeasureString(labelText, Font);
                g.DrawString(labelText, Font, Brushes.Gray, x - labelSize.Width / 2, chartArea.Bottom + 5);
            }
        }

        private class DataPoint
        {
            public double Value { get; set; }
            public DateTime Timestamp { get; set; }
        }
    }
}

