using System;
using System.Collections.Generic;
using System.Linq;
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
            int first=0;
            if(TrimLeadingMissing)while(first<samples.Count&&!samples[first].HasValue)first++;
            if(first>=samples.Count)return;
            if(samples.Count-first<2)return;
            double low=0, high=100;
            if(FitRange)
            {
                var values=samples.Where(n=>n.HasValue&&!Double.IsNaN(n.Value)&&!Double.IsInfinity(n.Value)).Select(n=>Math.Max(0,Math.Min(100,n.Value))).ToArray();
                if(values.Length>0)
                {
                    low=Math.Max(0,values.Min()-3);
                    high=Math.Min(100,Math.Max(low+10,values.Max()+3));
                    low=Math.Max(0,Math.Min(low,high-10));
                }
            }
            var geometry = new StreamGeometry();
            using(var context=geometry.Open()) {
                bool connected=false;
                for(int i=first;i<samples.Count;i++) {
                    double? value=samples[i];
                    if(!value.HasValue||Double.IsNaN(value.Value)||Double.IsInfinity(value.Value)){connected=false;continue;}
                    var point=new Point(w*(i-first)/(samples.Count-first-1),h-(h-2)*(Math.Max(low,Math.Min(high,value.Value))-low)/(high-low)-1);
                    if(!connected){context.BeginFigure(point,false,false);connected=true;}else context.LineTo(point,true,false);
                }
            }
            if(FillArea)
            {
                var area=new StreamGeometry();
                using(var context=area.Open())
                {
                    int start=first;
                    while(start<samples.Count)
                    {
                        while(start<samples.Count&&(!samples[start].HasValue||Double.IsNaN(samples[start].Value)||Double.IsInfinity(samples[start].Value)))start++;
                        if(start>=samples.Count)break;
                        int end=start;
                        while(end+1<samples.Count&&samples[end+1].HasValue&&!Double.IsNaN(samples[end+1].Value)&&!Double.IsInfinity(samples[end+1].Value))end++;
                        double left=w*(start-first)/(samples.Count-first-1);
                        context.BeginFigure(new Point(left,h),true,true);
                        for(int i=start;i<=end;i++)
                            context.LineTo(new Point(w*(i-first)/(samples.Count-first-1),h-(h-2)*(Math.Max(low,Math.Min(high,samples[i].Value))-low)/(high-low)-1),true,false);
                        context.LineTo(new Point(w*(end-first)/(samples.Count-first-1),h),true,false);
                        start=end+1;
                    }
                }
                area.Freeze();
                var brush=LineBrush.CloneCurrentValue();brush.Opacity=.16;
                dc.DrawGeometry(brush,null,area);
            }
            geometry.Freeze();
            var pen=new Pen(LineBrush,1.7){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            dc.DrawGeometry(null,pen,geometry);
        }
    }
}
