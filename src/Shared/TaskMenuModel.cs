using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Web.Script.Serialization;

namespace ReFix.Shared
{
    public enum TaskMenuEdge { Left, Top, Right, Bottom }
    public sealed class TaskMenuPanel
    {
        public string Id {get;set;} public string Monitor {get;set;} public TaskMenuEdge Edge {get;set;}
        public int Columns {get;set;} public int IconSize {get;set;} public List<string> Items {get;set;}
        public TaskMenuPanel(){Id=Guid.NewGuid().ToString("N");Monitor="";Columns=4;IconSize=40;Items=new List<string>();}
        public override string ToString(){return Monitor+" · "+Edge+" · "+Items.Count+" элементов";}
    }
    public sealed class TaskMenuConfig
    {
        public int Version {get;set;} public List<TaskMenuPanel> Panels {get;set;}
        public TaskMenuConfig(){Version=1;Panels=new List<TaskMenuPanel>();}
        public void Normalize()
        {
            Version=1;var clean=new List<TaskMenuPanel>();var ids=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var positions=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var counts=new Dictionary<string,int>(StringComparer.OrdinalIgnoreCase);
            foreach(var p in Panels??new List<TaskMenuPanel>()){
                if(p==null||String.IsNullOrWhiteSpace(p.Monitor)||!Enum.IsDefined(typeof(TaskMenuEdge),p.Edge))continue;
                int count;counts.TryGetValue(p.Monitor,out count);if(count>=3||!positions.Add(p.Monitor+"|"+p.Edge))continue;counts[p.Monitor]=count+1;
                if(String.IsNullOrWhiteSpace(p.Id)||!ids.Add(p.Id)){p.Id=Guid.NewGuid().ToString("N");ids.Add(p.Id);}
                p.Columns=Math.Max(1,Math.Min(12,p.Columns));p.IconSize=Math.Max(24,Math.Min(96,p.IconSize));
                var items=new List<string>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach(string item in p.Items??new List<string>()){string path=NormalizePath(item);if(path!=null&&seen.Add(path)&&items.Count<512)items.Add(path);}p.Items=items;clean.Add(p);
            }Panels=clean;
        }
        public static string NormalizePath(string value){try{if(String.IsNullOrWhiteSpace(value)||value.IndexOf('\0')>=0||!Path.IsPathRooted(value)||value.IndexOf("://",StringComparison.Ordinal)>=0)return null;bool drive=value.Length>=3&&Char.IsLetter(value[0])&&value[1]==':'&&(value[2]=='\\'||value[2]=='/');bool unc=value.StartsWith(@"\\",StringComparison.Ordinal)&&!value.StartsWith(@"\\?\",StringComparison.Ordinal)&&!value.StartsWith(@"\\.\",StringComparison.Ordinal);if(!drive&&!unc||value.IndexOf(':',drive?2:0)>=0)return null;string path=Path.GetFullPath(value);return path.TrimEnd(Path.DirectorySeparatorChar).Length>2?path.TrimEnd(Path.DirectorySeparatorChar):path;}catch(ArgumentException){return null;}catch(NotSupportedException){return null;}catch(PathTooLongException){return null;}}
    }
    public sealed class TaskMenuStore
    {
        public string FilePath {get;private set;}
        public TaskMenuStore(string root){if(String.IsNullOrWhiteSpace(root))throw new ArgumentException("Data root is required");FilePath=Path.Combine(Path.GetFullPath(root),"task-menu.json");}
        public TaskMenuConfig Load(){try{if(!File.Exists(FilePath)||new FileInfo(FilePath).Length>1048576)return new TaskMenuConfig();var config=new JavaScriptSerializer{MaxJsonLength=1048576}.Deserialize<TaskMenuConfig>(File.ReadAllText(FilePath))??new TaskMenuConfig();config.Normalize();return config;}catch(IOException){return new TaskMenuConfig();}catch(UnauthorizedAccessException){return new TaskMenuConfig();}catch(ArgumentException){return new TaskMenuConfig();}catch(InvalidOperationException){return new TaskMenuConfig();}}
        public void Save(TaskMenuConfig config){config.Normalize();Directory.CreateDirectory(Path.GetDirectoryName(FilePath));string temp=FilePath+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,new JavaScriptSerializer{MaxJsonLength=1048576}.Serialize(config));if(File.Exists(FilePath))File.Replace(temp,FilePath,null);else File.Move(temp,FilePath);}finally{if(File.Exists(temp))File.Delete(temp);}}
    }
    public static class TaskMenuGeometry
    {
        // Coordinates are physical desktop pixels, including negative monitor origins.
        public static TaskMenuEdge? TaskbarEdge(Rect monitor,Rect taskbar)
        {
            Rect overlap=Rect.Intersect(monitor,taskbar);if(overlap.IsEmpty||overlap.Width<=0||overlap.Height<=0)return null;
            bool horizontal=taskbar.Width>=taskbar.Height;
            if(horizontal&&overlap.Width>=monitor.Width*.5){if(Math.Abs(overlap.Top-monitor.Top)<=3)return TaskMenuEdge.Top;if(Math.Abs(overlap.Bottom-monitor.Bottom)<=3)return TaskMenuEdge.Bottom;}
            if(!horizontal&&overlap.Height>=monitor.Height*.5){if(Math.Abs(overlap.Left-monitor.Left)<=3)return TaskMenuEdge.Left;if(Math.Abs(overlap.Right-monitor.Right)<=3)return TaskMenuEdge.Right;}return null;
        }
        public static Rect Expanded(Rect monitor,Rect work,TaskMenuEdge edge,int count,int columns,int iconSize,double scale)
        {
            Rect area=Rect.Intersect(monitor,work);if(area.IsEmpty)area=monitor;scale=Math.Max(.5,Math.Min(4,scale));double cell=(Math.Max(24,Math.Min(96,iconSize))+16)*scale,pad=28*scale;
            int capacity=Math.Max(1,(int)Math.Floor((area.Width-pad)/cell));columns=Math.Max(1,Math.Min(Math.Min(Math.Min(12,columns),Math.Max(1,count)),capacity));
            int rows=Math.Max(1,(int)Math.Ceiling(Math.Max(1,count)/(double)columns));bool scrolling=rows*cell+pad>area.Height;if(scrolling){capacity=Math.Max(1,(int)Math.Floor((area.Width-pad-18*scale)/cell));columns=Math.Min(columns,capacity);rows=Math.Max(1,(int)Math.Ceiling(Math.Max(1,count)/(double)columns));}double width=Math.Min(area.Width,columns*cell+pad+(scrolling?18*scale:0)),height=Math.Min(area.Height,rows*cell+pad);
            double x=area.Left+(area.Width-width)/2,y=area.Top+(area.Height-height)/2;
            if(edge==TaskMenuEdge.Left)x=monitor.Left;if(edge==TaskMenuEdge.Right)x=monitor.Right-width;if(edge==TaskMenuEdge.Top)y=monitor.Top;if(edge==TaskMenuEdge.Bottom)y=monitor.Bottom-height;
            return new Rect(x,y,width,height);
        }
        public static Rect Sliver(Rect full,TaskMenuEdge edge,double size){size=Math.Max(2,Math.Min(size,Math.Min(full.Width,full.Height)));switch(edge){case TaskMenuEdge.Left:return new Rect(full.Left,full.Top,size,full.Height);case TaskMenuEdge.Right:return new Rect(full.Right-size,full.Top,size,full.Height);case TaskMenuEdge.Top:return new Rect(full.Left,full.Top,full.Width,size);default:return new Rect(full.Left,full.Bottom-size,full.Width,size);}}
    }
}
