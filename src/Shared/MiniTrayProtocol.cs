using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

namespace ReFix.Shared
{
    public class MiniTrayEntry
    {
        public int SchemaVersion {get;set;}
        public string Id {get;set;}
        public string Name {get;set;}
        public string Version {get;set;}
        public int Pid {get;set;}
        public string StartedUtc {get;set;}
        public string ExecutablePath {get;set;}
        public string WindowHandle {get;set;}
        public ThemeProfile IconTheme {get;set;}
    }
    public sealed class MiniTrayHost : MiniTrayEntry
    {
        public List<string> HiddenIds {get;set;}
        public string PopupWindowHandle {get;set;}
    }
    public static class MiniTrayFiles
    {
        public static readonly int LeftMessage=unchecked((int)RegisterWindowMessage("ReFIX.MiniTray.Left.v1"));
        public static readonly int RightMessage=unchecked((int)RegisterWindowMessage("ReFIX.MiniTray.Right.v1"));
        public static readonly int TransferMessage=unchecked((int)RegisterWindowMessage("ReFIX.MiniTray.Transfer.v1"));
        public static readonly int RestoreMessage=unchecked((int)RegisterWindowMessage("ReFIX.MiniTray.Restore.v1"));
        const int MaxBytes=32768;
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint RegisterWindowMessage(string value);
        [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
        [DllImport("user32.dll")] static extern bool IsWindow(IntPtr hwnd);
        [DllImport("user32.dll",SetLastError=true)] static extern bool PostMessage(IntPtr hwnd,int msg,IntPtr wparam,IntPtr lparam);
        public static bool ValidId(string id)
        {
            if(String.IsNullOrEmpty(id)||id.Length>64)return false;
            foreach(char c in id)if(!(c>='a'&&c<='z')&&!(c>='0'&&c<='9')&&c!='-')return false;
            return true;
        }
        // Reject reparse points on every existing component, including ancestors of the root.
        public static bool SafePath(string path)
        {
            try {string current=Path.GetFullPath(path);while(!String.IsNullOrEmpty(current)){
                if((Directory.Exists(current)||File.Exists(current))&&(File.GetAttributes(current)&FileAttributes.ReparsePoint)!=0)return false;
                string parent=Path.GetDirectoryName(current);if(parent==current)break;current=parent;
            }return true;}catch{return false;}
        }
        public static string TrayDirectory(string root){return Path.Combine(Path.GetFullPath(root),"tray");}
        static string EntryPath(string root,string id){if(!ValidId(id))throw new ArgumentException("Invalid tray entry ID");return Path.Combine(TrayDirectory(root),"entries",id+".json");}
        public static string HostPath(string root){return Path.Combine(TrayDirectory(root),"host.json");}
        static T Read<T>(string path) where T:class
        {
            try {if(!SafePath(path))return null;using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                if(stream.Length>MaxBytes)return null;using(var reader=new StreamReader(stream,Encoding.UTF8)){
                    char[] chars=new char[MaxBytes+1];int count=reader.ReadBlock(chars,0,chars.Length);if(count>MaxBytes)return null;
                    return new JavaScriptSerializer{MaxJsonLength=MaxBytes,RecursionLimit=8}.Deserialize<T>(new string(chars,0,count));
                }
            }}catch{return null;}
        }
        static void Write(string path,object record)
        {
            if(!SafePath(path))throw new IOException("Unsafe tray path");
            string directory=Path.GetDirectoryName(path);Directory.CreateDirectory(directory);
            if(!SafePath(path))throw new IOException("Unsafe tray path");
            byte[] bytes=Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(record));if(bytes.Length>MaxBytes)throw new ArgumentException("Tray record is too large");
            string temporary=Path.Combine(directory,"."+Guid.NewGuid().ToString("N")+".tmp");
            try {using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(bytes,0,bytes.Length);stream.Flush(true);}
                if(!SafePath(path))throw new IOException("Unsafe tray path");
                if(File.Exists(path))File.Replace(temporary,path,null);else File.Move(temporary,path);
            }finally{if(File.Exists(temporary)&&SafePath(temporary))File.Delete(temporary);}
        }
        public static MiniTrayEntry CreateEntry(string id,string name,string version,IntPtr hwnd)
        {
            if(!ValidId(id))throw new ArgumentException("Invalid tray entry ID");
            using(var process=Process.GetCurrentProcess())return new MiniTrayEntry{SchemaVersion=1,Id=id,Name=name,Version=version,Pid=process.Id,StartedUtc=process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),ExecutablePath=process.MainModule.FileName,WindowHandle=hwnd.ToInt64().ToString(CultureInfo.InvariantCulture)};
        }
        public static IntPtr Handle(MiniTrayEntry entry)
        {
            long value;if(entry==null||!Int64.TryParse(entry.WindowHandle,NumberStyles.Integer,CultureInfo.InvariantCulture,out value)||value==0||(IntPtr.Size==4&&(value>Int32.MaxValue||value<Int32.MinValue)))return IntPtr.Zero;return new IntPtr(value);
        }
        public static bool IsAlive(MiniTrayEntry entry)
        {
            try {
                long ticks;IntPtr hwnd=Handle(entry);
                if(entry==null||entry.SchemaVersion!=1||!ValidId(entry.Id)||entry.Pid<=0||!Int64.TryParse(entry.StartedUtc,out ticks)||hwnd==IntPtr.Zero||!IsWindow(hwnd)||String.IsNullOrEmpty(entry.ExecutablePath)||entry.ExecutablePath.Length>2048||entry.Name==null||entry.Name.Length>256||entry.Version==null||entry.Version.Length>64)return false;
                uint pid;GetWindowThreadProcessId(hwnd,out pid);if(pid!=(uint)entry.Pid)return false;
                using(var process=Process.GetProcessById(entry.Pid))return !process.HasExited&&process.StartTime.ToUniversalTime().Ticks==ticks&&String.Equals(Path.GetFullPath(process.MainModule.FileName),Path.GetFullPath(entry.ExecutablePath),StringComparison.OrdinalIgnoreCase);
            }catch{return false;}
        }
        public static IList<MiniTrayEntry> ReadEntries(string root)
        {
            var result=new List<MiniTrayEntry>();string directory=Path.Combine(TrayDirectory(root),"entries");
            try{if(!SafePath(directory)||!Directory.Exists(directory))return result;int count=0;foreach(string path in Directory.EnumerateFiles(directory,"*.json")){
                if(++count>256)break;MiniTrayEntry entry=Read<MiniTrayEntry>(path);
                if(entry!=null&&String.Equals(Path.GetFileNameWithoutExtension(path),entry.Id,StringComparison.Ordinal)&&IsAlive(entry))result.Add(entry);
            }}catch(IOException){}catch(UnauthorizedAccessException){}return result;
        }
        public static MiniTrayHost ReadHost(string root)
        {
            MiniTrayHost host=Read<MiniTrayHost>(HostPath(root));if(host==null||host.HiddenIds==null||host.HiddenIds.Count>256||!IsAlive(host))return null;
            foreach(string id in host.HiddenIds)if(!ValidId(id))return null;return host;
        }
        public static void WriteEntry(string root,MiniTrayEntry entry){if(!IsAlive(entry))throw new ArgumentException("Tray entry is not live");Write(EntryPath(root,entry.Id),entry);}
        public static void PublishHost(string root,IntPtr hwnd,IEnumerable<string> hiddenIds,IntPtr popup=default(IntPtr))
        {
            MiniTrayEntry identity=CreateEntry("mini-icons","Mini Icons","1",hwnd);var ids=new List<string>();
            if(hiddenIds!=null)foreach(string id in hiddenIds){if(!ValidId(id)||ids.Count>=256)throw new ArgumentException("Invalid hidden tray IDs");if(!ids.Contains(id))ids.Add(id);}
            var host=new MiniTrayHost{SchemaVersion=1,Id=identity.Id,Name=identity.Name,Version=identity.Version,Pid=identity.Pid,StartedUtc=identity.StartedUtc,ExecutablePath=identity.ExecutablePath,WindowHandle=identity.WindowHandle,HiddenIds=ids,PopupWindowHandle=popup.ToInt64().ToString(CultureInfo.InvariantCulture)};
            if(!IsAlive(host))throw new ArgumentException("Tray host is not live");Write(HostPath(root),host);
        }
        static bool Own(MiniTrayEntry entry)
        {
            if(entry==null)return false;using(var process=Process.GetCurrentProcess())return entry.Pid==process.Id&&entry.StartedUtc==process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture)&&String.Equals(entry.ExecutablePath,process.MainModule.FileName,StringComparison.OrdinalIgnoreCase);
        }
        public static void RemoveHost(string root){string path=HostPath(root);if(SafePath(path)&&Own(Read<MiniTrayHost>(path)))try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
        public static void RemoveEntry(string root,MiniTrayEntry entry){string path=EntryPath(root,entry.Id);MiniTrayEntry current=Read<MiniTrayEntry>(path);if(SafePath(path)&&Own(current)&&current.WindowHandle==entry.WindowHandle&&current.StartedUtc==entry.StartedUtc)try{File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){}}
        public static bool Invoke(MiniTrayEntry entry,bool right,int x,int y){return IsAlive(entry)&&PostMessage(Handle(entry),right?RightMessage:LeftMessage,new IntPtr(x),new IntPtr(y));}
        public static bool RequestTransfer(MiniTrayEntry entry,MiniTrayHost host,bool restore){return IsAlive(entry)&&IsAlive(host)&&PostMessage(Handle(host),restore?RestoreMessage:TransferMessage,Handle(entry),IntPtr.Zero);}
        public static bool WindowBelongsTo(IntPtr hwnd,int processId){uint pid;return hwnd!=IntPtr.Zero&&IsWindow(hwnd)&&GetWindowThreadProcessId(hwnd,out pid)!=0&&pid==(uint)processId;}
    }
}
