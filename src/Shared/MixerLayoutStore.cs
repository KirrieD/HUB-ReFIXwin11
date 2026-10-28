using System;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
namespace ReFix.Shared
{
    public sealed class MixerLayoutStore : IDisposable
    {
        readonly object gate=new object(); readonly string file; readonly FileSystemWatcher watcher; readonly Timer reload;
        MixerLayoutProfile current; string fingerprint; bool disposed; int retries;
        public event EventHandler Changed;
        public MixerLayoutProfile Current { get { lock(gate) return current.Clone(); } }
        public MixerLayoutStore(string root)
        {
            if(String.IsNullOrWhiteSpace(root)) throw new ArgumentException("A data root is required.","root");
            root=Path.GetFullPath(root); Directory.CreateDirectory(root); file=Path.Combine(root,"mixer-layout.json");
            current=Read()??new MixerLayoutProfile(); fingerprint=Serialize(current);
            reload=new Timer(Reload,null,Timeout.Infinite,Timeout.Infinite);
            watcher=new FileSystemWatcher(root,"mixer-layout.json"); watcher.NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size;
            watcher.Changed+=OnFile; watcher.Created+=OnFile; watcher.Deleted+=OnFile; watcher.Renamed+=OnFile; watcher.Error+=OnError; watcher.EnableRaisingEvents=true;
        }
        static string Serialize(MixerLayoutProfile p) { return new JavaScriptSerializer().Serialize(p); }
        MixerLayoutProfile Read()
        {
            try {
                if(!File.Exists(file)) return new MixerLayoutProfile();
                using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                    if(stream.Length>65536) return null;
                    using(var reader=new StreamReader(stream)) { var p=new JavaScriptSerializer { MaxJsonLength=65536 }.Deserialize<MixerLayoutProfile>(reader.ReadToEnd()); if(p!=null)p.Normalize(); return p; }
                }
            } catch(IOException) { return null; } catch(UnauthorizedAccessException) { return null; } catch(ArgumentException) { return null; } catch(InvalidOperationException) { return null; }
        }
        public void Save(MixerLayoutProfile value)
        {
            if(value==null)throw new ArgumentNullException("value");
            value=value.Clone();value.Normalize(); string json=Serialize(value); bool changed;
            lock(gate) {
                if(disposed)throw new ObjectDisposedException("MixerLayoutStore");
                string temp=file+"."+Guid.NewGuid().ToString("N")+".tmp";
                try { File.WriteAllText(temp,json); if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file); }
                finally { if(File.Exists(temp))File.Delete(temp); }
                changed=json!=fingerprint; current=value; fingerprint=json;
            }
            if(changed)RaiseChanged();
        }
        public void ResetDefaults() { Save(new MixerLayoutProfile()); }
        void OnFile(object sender,FileSystemEventArgs e) { Schedule(); }
        void OnError(object sender,ErrorEventArgs e) { Schedule(); }
        void Schedule() { lock(gate) { if(disposed)return; retries=0;reload.Change(100,Timeout.Infinite); } }
        void Reload(object state)
        {
            bool changed=false;
            lock(gate) {
                if(disposed)return; var p=Read();
                if(p==null) { if(++retries<4)reload.Change(120,Timeout.Infinite); return; }
                string json=Serialize(p); changed=json!=fingerprint; if(changed){current=p;fingerprint=json;}
            }
            if(changed)RaiseChanged();
        }
        void RaiseChanged() { EventHandler handler;lock(gate){if(disposed)return;handler=Changed;}if(handler!=null)handler(this,EventArgs.Empty); }
        public void Dispose() { lock(gate){if(disposed)return;disposed=true;watcher.EnableRaisingEvents=false;watcher.Dispose();reload.Dispose();} }
    }
}
