using System;
namespace ReFix.Shared
{
    public sealed class MixerLayoutProfile
    {
        public double WindowWidth { get; set; }
        public double WindowHeight { get; set; }
        public double BodyFontSize { get; set; }
        public double TitleFontSize { get; set; }
        // Zero stretches the slider to the available row width.
        public double SliderWidth { get; set; }
        public double SliderHeight { get; set; }
        public double TrackThickness { get; set; }
        public double ThumbSize { get; set; }
        public double RowSpacing { get; set; }
        public double IconSize { get; set; }
        public MixerLayoutProfile()
        {
            WindowWidth=340; WindowHeight=420; BodyFontSize=12; TitleFontSize=18;
            SliderWidth=0; SliderHeight=24; TrackThickness=3; ThumbSize=16; RowSpacing=8; IconSize=24;
        }
        public MixerLayoutProfile Clone() { return (MixerLayoutProfile)MemberwiseClone(); }
        public void Normalize()
        {
            WindowWidth=Bound(WindowWidth,280,900,340); WindowHeight=Bound(WindowHeight,260,1100,420);
            BodyFontSize=Bound(BodyFontSize,10,24,12); TitleFontSize=Bound(TitleFontSize,14,32,18);
            SliderWidth=Bound(SliderWidth,0,800,0); SliderHeight=Bound(SliderHeight,16,60,24);if(SliderWidth>0)SliderWidth=Math.Max(80,SliderWidth);
            TrackThickness=Bound(TrackThickness,1,12,3); ThumbSize=Bound(ThumbSize,10,40,16);
            SliderHeight=Math.Max(SliderHeight,ThumbSize); TrackThickness=Math.Min(TrackThickness,ThumbSize);
            RowSpacing=Bound(RowSpacing,0,28,8); IconSize=Bound(IconSize,16,40,24);
        }
        static double Bound(double n,double min,double max,double fallback) { return Double.IsNaN(n)||Double.IsInfinity(n)?fallback:Math.Max(min,Math.Min(max,n)); }
    }
}
