using System;
using System.Windows;
using ReFix.Shared;
using ReFix.Hub;
namespace ReFix.Tests
{
    internal static class HubPresentationTests
    {
        internal static void Run(Action<string,bool> report)
        {
            var work=new Rect(0,0,1920,1080);
            var left=SettingsPlacement.Calculate(work,new Rect(1100,100,800,700),500,500,420,800);
            report("settings choose larger free left side",left.OnLeft&&left.Child.Right+10<=left.Owner.Left&&work.Contains(left.Child));
            var right=SettingsPlacement.Calculate(work,new Rect(20,100,840,700),500,500,420,840);
            report("settings choose larger free right side",!right.OnLeft&&right.Child.Left>=right.Owner.Right+10&&work.Contains(right.Child));
            var cramped=SettingsPlacement.Calculate(new Rect(0,0,1366,768),new Rect(150,10,1060,720),640,580,420,840);
            report("settings fit beside HUB on ordinary narrow screen",cramped.Child.Left>=cramped.Owner.Right+10&&cramped.Child.Width>=420&&new Rect(0,0,1366,768).Contains(cramped.Child));
            var tinyWork=new Rect(0,0,1024,728);var tiny=SettingsPlacement.Calculate(tinyWork,new Rect(0,0,1060,720),720,760,520,840);
            report("settings remain within small screen when both minimum widths cannot fit",tinyWork.Contains(tiny.Child)&&tinyWork.Contains(tiny.Owner));
            var negative=SettingsPlacement.Calculate(new Rect(-1920,69,1920,1080),new Rect(-1800,200,1060,700),640,580,420,840);
            report("settings placement supports negative monitor coordinates",negative.Child.Left>=negative.Owner.Right+10&&negative.Child.Right<=0&&negative.Child.Top>=69);
            var custom=new ThemeProfile{Mode="Custom",Tertiary="#123456"};custom.Normalize();
            report("tertiary text is configurable and retained by clone",custom.Clone().Tertiary=="#123456"&&ThemeProfile.Resolve(custom).Tertiary=="#123456");
            custom.Tertiary="bad";custom.Normalize();report("invalid tertiary color receives readable default",custom.Tertiary=="#FFD37A");
            var system=ThemeProfile.Resolve(new ThemeProfile());report("system tertiary text contrasts with background",Contrast(system.Tertiary,system.Background)>=4.5);
        }
        static double Contrast(string first,string second){double a=Luminance(first),b=Luminance(second);return (Math.Max(a,b)+.05)/(Math.Min(a,b)+.05);}
        static double Luminance(string hex){double[] c={Convert.ToInt32(hex.Substring(1,2),16)/255d,Convert.ToInt32(hex.Substring(3,2),16)/255d,Convert.ToInt32(hex.Substring(5,2),16)/255d};for(int i=0;i<3;i++)c[i]=c[i]<=.04045?c[i]/12.92:Math.Pow((c[i]+.055)/1.055,2.4);return .2126*c[0]+.7152*c[1]+.0722*c[2];}
    }
}
