using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace ReFix.SystemMonitor
{
    // One retained drawing per sample update. Missing samples break the line.
    internal sealed class Sparkline : FrameworkElement
    {
        IList<double?> samples = new List<double?>();
        public Brush LineBrush { get; set; }
        public bool ShowGrid { get; set; }
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
            if(samples.Count<2)return;
            var geometry = new StreamGeometry();
            using(var context=geometry.Open()) {
                bool connected=false;
                for(int i=0;i<samples.Count;i++) {
                    double? value=samples[i];
                    if(!value.HasValue||Double.IsNaN(value.Value)||Double.IsInfinity(value.Value)){connected=false;continue;}
                    var point=new Point(w*i/(samples.Count-1),h-(h-2)*Math.Max(0,Math.Min(100,value.Value))/100-1);
                    if(!connected){context.BeginFigure(point,false,false);connected=true;}else context.LineTo(point,true,false);
                }
            }
            geometry.Freeze();dc.DrawGeometry(null,new Pen(LineBrush,1.5),geometry);
        }
    }
}
