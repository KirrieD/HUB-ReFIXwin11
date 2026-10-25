using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace ReFix.Shared
{
    public static class Paths
    {
        public static string Root { get { string path=Environment.GetEnvironmentVariable("REFIX_DATA_ROOT"); return Path.GetFullPath(String.IsNullOrWhiteSpace(path)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"ReFIXHub"):path); } }
        public static string ThemeFile { get { return Path.Combine(Root,"theme.json"); } }
        public static string Modules { get { return Path.Combine(Root,"modules"); } }
        public static string State { get { return Path.Combine(Root,"state"); } }
        public static void Ensure() { Directory.CreateDirectory(Root);Directory.CreateDirectory(Modules);Directory.CreateDirectory(State); }
    }
    public sealed class ThemeProfile
    {
        public string Mode {get;set;} public string Background {get;set;} public string Surface {get;set;}
        public string Foreground {get;set;} public string Muted {get;set;} public string Accent {get;set;} public string Border {get;set;} public string Glow {get;set;}
        public double BackgroundOpacity {get;set;} public double GlowOpacity {get;set;} public double GlowRadius {get;set;} public double CornerRadius {get;set;} public double FontSize {get;set;}
        public string ImagePath {get;set;} public double ImageOpacity {get;set;} public double ImageBlur {get;set;} public string ImageFit {get;set;} public double ImageX {get;set;} public double ImageY {get;set;}
        public ThemeProfile() { Mode="System";Background="#202020";Surface="#2D2D2D";Foreground="#F3F3F3";Muted="#A8A8A8";Accent="#60CDFF";Border="#414141";Glow="#60CDFF";BackgroundOpacity=1;GlowOpacity=0;GlowRadius=12;CornerRadius=12;FontSize=13;ImagePath="";ImageOpacity=0.25;ImageBlur=0;ImageFit="UniformToFill";ImageX=0.5;ImageY=0.5; }
        public ThemeProfile Clone() { return (ThemeProfile)MemberwiseClone(); }
        static string Color(string value,string fallback) { return value!=null&&Regex.IsMatch(value,"^#[0-9a-fA-F]{6}$")?value.ToUpperInvariant():fallback; }
        static double Range(double v,double min,double max,double fallback) { return Double.IsNaN(v)||Double.IsInfinity(v)?fallback:Math.Max(min,Math.Min(max,v)); }
        public void Normalize() {
            Mode=String.Equals(Mode,"Custom",StringComparison.OrdinalIgnoreCase)?"Custom":"System";
            Background=Color(Background,"#202020");Surface=Color(Surface,"#2D2D2D");Foreground=Color(Foreground,"#F3F3F3");Muted=Color(Muted,"#A8A8A8");Accent=Color(Accent,"#60CDFF");Border=Color(Border,"#414141");Glow=Color(Glow,"#60CDFF");
            BackgroundOpacity=Range(BackgroundOpacity,0,1,1);GlowOpacity=Range(GlowOpacity,0,1,0);GlowRadius=Range(GlowRadius,0,40,12);CornerRadius=Range(CornerRadius,0,24,12);FontSize=Range(FontSize,11,20,13);
            ImageOpacity=Range(ImageOpacity,0,1,.25);ImageBlur=Range(ImageBlur,0,30,0);ImageX=Range(ImageX,0,1,.5);ImageY=Range(ImageY,0,1,.5);ImagePath=ImagePath??"";
            if(ImageFit!="Uniform"&&ImageFit!="Fill")ImageFit="UniformToFill";
        }
        static string Hex(System.Windows.Media.Color c) { return String.Format("#{0:X2}{1:X2}{2:X2}",c.R,c.G,c.B); }
        public static ThemeProfile Resolve(ThemeProfile value) {
            ThemeProfile p=(value??new ThemeProfile()).Clone();p.Normalize();if(p.Mode!="System")return p;
            p.BackgroundOpacity=1;p.GlowOpacity=0;p.ImagePath="";p.FontSize=SystemFonts.MessageFontSize;p.CornerRadius=SystemParameters.HighContrast?0:8;
            if(SystemParameters.HighContrast) {p.Background=Hex(SystemColors.WindowColor);p.Surface=p.Background;p.Foreground=Hex(SystemColors.WindowTextColor);p.Muted=p.Foreground;p.Accent=Hex(SystemColors.HighlightColor);p.Border=p.Foreground;p.Glow=p.Accent;return p;}
            bool light=false;
            using(RegistryKey key=Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize")) { if(key!=null)light=Convert.ToInt32(key.GetValue("AppsUseLightTheme",0))!=0; }
            p.Background=light?"#F3F3F3":"#202020";p.Surface=light?"#FFFFFF":"#2D2D2D";p.Foreground=light?"#1A1A1A":"#F3F3F3";p.Muted=light?"#5E5E5E":"#A8A8A8";p.Border=light?"#D0D0D0":"#414141";
            p.Accent=Hex(SystemParameters.WindowGlassColor);p.Glow=p.Accent;return p;
        }
    }
}
