using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
namespace ReFix.Shared
{
    public static class MiniIconsGeometry
    {
        public static TaskMenuEdge TaskbarSide(System.Windows.Rect monitor,System.Windows.Rect work){double[] gaps={work.Left-monitor.Left,work.Top-monitor.Top,monitor.Right-work.Right,monitor.Bottom-work.Bottom};int side=3;for(int i=0;i<4;i++)if(gaps[i]>gaps[side])side=i;return (TaskMenuEdge)side;}
        public static System.Windows.Rect Edge(System.Windows.Rect monitor,System.Windows.Rect work,double scale){double width=Math.Min(work.Width,26*Math.Max(.5,Math.Min(4,scale))),height=Math.Min(work.Height,40*Math.Max(.5,Math.Min(4,scale)));var side=TaskbarSide(monitor,work);return new System.Windows.Rect(side==TaskMenuEdge.Left?work.Left:work.Right-width,side==TaskMenuEdge.Top?work.Top:work.Bottom-height,width,height);}
        public static System.Windows.Rect Popup(System.Windows.Rect monitor,System.Windows.Rect work,System.Windows.Rect edge,System.Windows.Size size,double gap){double width=Math.Min(size.Width,work.Width),height=Math.Min(size.Height,work.Height);var side=TaskbarSide(monitor,work);double x=edge.Right-width,y=edge.Top-height-gap;if(side==TaskMenuEdge.Top)y=edge.Bottom+gap;if(side==TaskMenuEdge.Left){x=edge.Right+gap;y=edge.Bottom-height;}if(side==TaskMenuEdge.Right){x=edge.Left-width-gap;y=edge.Bottom-height;}return new System.Windows.Rect(Math.Max(work.Left,Math.Min(x,work.Right-width)),Math.Max(work.Top,Math.Min(y,work.Bottom-height)),width,height);}
    }
    public sealed class MiniIconsConfig
    {
        public int SchemaVersion{get;set;}public string Monitor{get;set;}public int IconSize{get;set;}public int Columns{get;set;}
        public bool ShowWindowsTray{get;set;}
        public List<string> OwnTransfers{get;set;}public List<string> OwnOrder{get;set;}
        public MiniIconsConfig(){SchemaVersion=2;Monitor="";IconSize=16;Columns=2;OwnTransfers=new List<string>{"hub","quiet-mixer","task-menu","quick-note"};OwnOrder=new List<string>(OwnTransfers);}
        public void Normalize(){if(SchemaVersion<2&&IconSize==24&&Columns==5){IconSize=16;Columns=2;}SchemaVersion=2;Monitor=(Monitor??"").Substring(0,Math.Min(256,(Monitor??"").Length));IconSize=Math.Max(16,Math.Min(32,IconSize));Columns=Math.Max(1,Math.Min(8,Columns));OwnTransfers=Clean(OwnTransfers);OwnOrder=Clean(OwnOrder);}
        static List<string> Clean(IEnumerable<string> list){return (list??new string[0]).Where(MiniTrayFiles.ValidId).Distinct(StringComparer.Ordinal).Take(256).ToList();}
        public MiniIconsConfig Clone(){var p=new MiniIconsConfig{Monitor=Monitor,IconSize=IconSize,Columns=Columns,ShowWindowsTray=ShowWindowsTray,OwnTransfers=OwnTransfers==null?null:new List<string>(OwnTransfers),OwnOrder=OwnOrder==null?null:new List<string>(OwnOrder)};p.Normalize();return p;}
    }
    public sealed class MiniIconsStore
    {
        public string FilePath{get;private set;}
        public MiniIconsStore(string root){FilePath=Path.Combine(Path.GetFullPath(root),"mini-icons.json");}
        public MiniIconsConfig Load(){try{if(!File.Exists(FilePath)||new FileInfo(FilePath).Length>65536)return new MiniIconsConfig();var p=new JavaScriptSerializer{MaxJsonLength=65536,RecursionLimit=8}.Deserialize<MiniIconsConfig>(File.ReadAllText(FilePath))??new MiniIconsConfig();p.Normalize();return p;}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(InvalidOperationException){}return new MiniIconsConfig();}
        public void Save(MiniIconsConfig value){var p=value.Clone();if(!MiniTrayFiles.SafePath(FilePath))throw new IOException("Unsafe mini-icons settings path");Directory.CreateDirectory(Path.GetDirectoryName(FilePath));string temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,new JavaScriptSerializer().Serialize(p));if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);}finally{if(File.Exists(temp))File.Delete(temp);}}
    }
}
