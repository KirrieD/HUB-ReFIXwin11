using System;
using System.Collections.Generic;
using System.IO;
using System.Web.Script.Serialization;

namespace ReFix.SystemMonitor
{
    public sealed class WindowPlacement
    {
        public int X { get; set; }
        public int Y { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
        public bool Positioned { get; set; }
    }

    public sealed class MonitorConfig
    {
        public int IntervalSeconds { get; set; }
        public int HistorySeconds { get; set; }
        public bool BackgroundHistory { get; set; }
        public bool Topmost { get; set; }
        public double Opacity { get; set; }
        public double Scale { get; set; }
        public List<string> VisibleKinds { get; set; }
        public Dictionary<string,string> Colors { get; set; }
        public WindowPlacement Compact { get; set; }
        public Dictionary<string,WindowPlacement> Charts { get; set; }
        public MonitorConfig()
        {
            IntervalSeconds=1;HistorySeconds=120;Opacity=1;Scale=1;
            VisibleKinds=new List<string>{"cpu","gpu","memory","disk"};
            Colors=new Dictionary<string,string>{{"cpu","#42A5FF"},{"gpu","#B68AFF"},{"memory","#4ADBC8"},{"disk","#E6BA57"}};
            Compact=new WindowPlacement();
            Charts=new Dictionary<string,WindowPlacement>();
        }
        internal void Normalize()
        {
            IntervalSeconds=Math.Max(1,Math.Min(10,IntervalSeconds));
            HistorySeconds=Math.Max(30,Math.Min(1200,HistorySeconds));
            Opacity=Double.IsNaN(Opacity)?1:Math.Max(.3,Math.Min(1,Opacity));
            Scale=Double.IsNaN(Scale)?1:Math.Max(.7,Math.Min(2,Scale));
            if(VisibleKinds==null)VisibleKinds=new MonitorConfig().VisibleKinds;
            if(Colors==null)Colors=new MonitorConfig().Colors;
            foreach(var pair in new MonitorConfig().Colors)
            {
                string color;
                if(!Colors.TryGetValue(pair.Key,out color)||!System.Text.RegularExpressions.Regex.IsMatch(color??"","^#[0-9a-fA-F]{6}$"))Colors[pair.Key]=pair.Value;
            }
            if(Compact==null)Compact=new WindowPlacement();
            if(Charts==null)Charts=new Dictionary<string,WindowPlacement>();
        }
    }

    internal sealed class ConfigStore
    {
        readonly string path;
        internal ConfigStore(string root){path=Path.Combine(root,"system-monitor","settings.json");}
        internal MonitorConfig Load()
        {
            if(!File.Exists(path))return new MonitorConfig();
            try
            {
                var value=new JavaScriptSerializer().Deserialize<MonitorConfig>(File.ReadAllText(path));
                if(value==null)throw new InvalidDataException("Пустые настройки мониторинга.");
                value.Normalize();return value;
            }
            catch(ArgumentException){return new MonitorConfig();}
            catch(InvalidOperationException){return new MonitorConfig();}
        }
        internal void Save(MonitorConfig value)
        {
            value.Normalize();Directory.CreateDirectory(Path.GetDirectoryName(path));
            string temporary=path+"."+Guid.NewGuid().ToString("N")+".tmp";
            try
            {
                File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(value));
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            }
            finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
    }
}
