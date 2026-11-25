using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace ReFix.SystemMonitor
{
    // One retained drawing per sample update. Missing history slots preserve spacing between valid readings.
    internal sealed class Sparkline : FrameworkElement
    {
        IList<double?> samples = new List<double?>();
        public Brush LineBrush { get; set; }
        public bool ShowGrid { get; set; }
        public bool TrimLeadingMissing { get; set; }
        public bool FillArea { get; set; }
        public bool FitRange { get; set; }
        public Sparkline() { LineBrush = Brushes.DeepSkyBlue; MinHeight = 18; }
        public void Update(IList<double?> values) { samples = values ?? new List<double?>(); InvalidateVisual(); }
        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double w = ActualWidth, h = ActualHeight;
            if (w < 1 || h < 1) return;
            if (ShowGrid) {
                Brush grid = TryFindResource("Hub.Border") as Brush ?? Brushes.Gray;
                var gridPen = new Pen(grid, .5);
                for (int i=1;i<4;i++) dc.DrawLine(gridPen,new Point(0,h*i/4),new Point(w,h*i/4));
            }
            int first = 0;
            while (first < samples.Count && !Valid(samples[first])) first++;
            if (first >= samples.Count - 1) return;

            double low = 0, high = 100;
            if (FitRange)
            {
                var values = samples.Where(Valid).Select(n => Math.Max(0, Math.Min(100, n.Value))).ToArray();
                low = Math.Max(0, values.Min() - 3);
                high = Math.Min(100, Math.Max(low + 10, values.Max() + 3));
                low = Math.Max(0, Math.Min(low, high - 10));
            }

            // Keep the time axis; connect the real readings across empty slots.
            int origin = TrimLeadingMissing ? first : 0;
            var points = new List<Point>();
            for (int i = first; i < samples.Count; i++)
            {
                double? value = samples[i];
                if (!Valid(value)) continue;
                double x = w * (i - origin) / (samples.Count - origin - 1);
                double fraction = (Math.Max(low, Math.Min(high, value.Value)) - low) / (high - low);
                points.Add(new Point(x, h - (h - 2) * fraction - 1));
            }
            if (points.Count < 2) return;

            var line = new StreamGeometry();
            using (var context = line.Open())
            {
                context.BeginFigure(points[0], false, false);
                for (int i = 1; i < points.Count; i++) context.LineTo(points[i], true, false);
            }
            line.Freeze();

            if (FillArea)
            {
                var area = new StreamGeometry();
                using (var context = area.Open())
                {
                    context.BeginFigure(new Point(points[0].X, h), true, true);
                    context.PolyLineTo(points, true, false);
                    context.LineTo(new Point(points[points.Count - 1].X, h), true, false);
                }
                area.Freeze();
                var brush = LineBrush.CloneCurrentValue();
                brush.Opacity = .16;
                dc.DrawGeometry(brush, null, area);
            }

            var pen = new Pen(LineBrush, 1.7)
            {
                StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round
            };
            dc.DrawGeometry(null, pen, line);
        }

        static bool Valid(double? value)
        {
            return value.HasValue && !Double.IsNaN(value.Value) && !Double.IsInfinity(value.Value);
        }
    }
}
