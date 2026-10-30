using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ReFix.Shared
{
    public sealed class ThemeStore : IDisposable
    {
        public sealed class ScopeSettings { public bool Enabled {get;set;} public ThemeProfile Profile {get;set;} }
        readonly string root,file,globalFile,scope; readonly object gate=new object(); readonly FileSystemWatcher watcher;
        ThemeProfile current,pending; ScopeSettings scoped; bool disposed,writeScheduled,systemSubscribed; int writeRetries,readRetries; string lastError; readonly Timer saveTimer,readTimer;
        public event EventHandler Changed;
        public string Root {get{return root;}} public string Scope {get{return scope;}}
        public ThemeProfile Current {get{lock(gate)return (scope!=null&&current.Mode=="Custom"&&scoped.Enabled&&scoped.Profile!=null?scoped.Profile:current).Clone();}}
        public ThemeProfile GlobalCurrent {get{lock(gate)return current.Clone();}}
        public ThemeProfile ScopedCurrent {get{lock(gate)return (scoped.Profile??current).Clone();}}
        public bool ScopeOverrideEnabled {get{lock(gate)return scope!=null&&scoped.Enabled;}}
        public string LastError {get{lock(gate)return lastError;}}
        public ThemeStore(string root):this(root,null){}
        public ThemeStore(string root,string scope) {
            if(String.IsNullOrWhiteSpace(root))throw new ArgumentException("A data root is required","root");
            if(scope!=null&&scope!="quiet-mixer"&&scope!="task-menu"&&scope!="mini-icons")throw new ArgumentException("Unknown theme scope","scope");
            this.root=Path.GetFullPath(root);this.scope=scope;Directory.CreateDirectory(this.root);globalFile=Path.Combine(this.root,"theme.json");file=scope==null?globalFile:Path.Combine(this.root,"theme."+scope+".json");
            current=Read<ThemeProfile>(globalFile)??new ThemeProfile();current.Normalize();scoped=(scope==null?null:Read<ScopeSettings>(file))??new ScopeSettings();NormalizeScope(scoped);
            saveTimer=new Timer(WritePending,null,Timeout.Infinite,Timeout.Infinite);readTimer=new Timer(Reload,null,Timeout.Infinite,Timeout.Infinite);
            watcher=new FileSystemWatcher(this.root,"theme*.json");watcher.NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size;watcher.Changed+=OnFile;watcher.Created+=OnFile;watcher.Renamed+=OnFile;watcher.Deleted+=OnFile;watcher.EnableRaisingEvents=true;
            SystemEvents.UserPreferenceChanged+=OnPreference;
            // Background startup must not load WPF just to watch theme files. Subscribe before
            // scanning so a concurrent first WPF load cannot slip between the two operations.
            AppDomain.CurrentDomain.AssemblyLoad+=OnAssemblyLoad;
            foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())
                if(assembly.GetName().Name=="PresentationFramework"){AttachSystemProperties();break;}
        }
        void OnAssemblyLoad(object sender,AssemblyLoadEventArgs e)
        {
            if(e.LoadedAssembly.GetName().Name=="PresentationFramework")AttachSystemProperties();
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void AttachSystemProperties()
        {
            lock(gate){if(disposed||systemSubscribed)return;System.Windows.SystemParameters.StaticPropertyChanged+=OnSystemProperty;systemSubscribed=true;}
        }
        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        void DetachSystemProperties(){System.Windows.SystemParameters.StaticPropertyChanged-=OnSystemProperty;systemSubscribed=false;}
        static void NormalizeScope(ScopeSettings p){if(p.Profile!=null){p.Profile.Normalize();p.Profile.Mode="Custom";}}
        T Read<T>(string path) where T:class {
            try{using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){
                if(stream.Length>65536)return null;
                using(var reader=new StreamReader(stream,System.Text.Encoding.UTF8)){var text=new char[65537];int count=reader.ReadBlock(text,0,text.Length);if(count>65536)return null;return new JavaScriptSerializer().Deserialize<T>(new string(text,0,count));}
            }}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
        }
        void Raise(){EventHandler handler=Changed;if(handler!=null)ThreadPool.QueueUserWorkItem(delegate{lock(gate){if(disposed)return;}handler(this,EventArgs.Empty);});}
        void OnPreference(object sender,UserPreferenceChangedEventArgs e){Raise();}
        void OnSystemProperty(object sender,System.ComponentModel.PropertyChangedEventArgs e){Raise();}
        void OnFile(object sender,FileSystemEventArgs e){
            var renamed=e as RenamedEventArgs;if(e.FullPath!=globalFile&&e.FullPath!=file&&(renamed==null||(renamed.OldFullPath!=file&&renamed.OldFullPath!=globalFile)))return;
            lock(gate){if(disposed)return;readRetries=0;readTimer.Change(80,Timeout.Infinite);}
        }
        void Reload(object state){bool changed=false;lock(gate){
            if(disposed)return;var serializer=new JavaScriptSerializer();string before=serializer.Serialize(current)+serializer.Serialize(scoped);bool retry=false;
            if(scope!=null||pending==null){var next=Read<ThemeProfile>(globalFile);if(next!=null){next.Normalize();current=next;}else if(!File.Exists(globalFile))current=new ThemeProfile();else retry=true;}
            if(scope!=null&&pending==null){var next=Read<ScopeSettings>(file);if(next!=null){NormalizeScope(next);scoped=next;}else if(!File.Exists(file))scoped=new ScopeSettings();else retry=true;}
            changed=before!=serializer.Serialize(current)+serializer.Serialize(scoped);
            // Bounded one-shot retries cover transient locks and replacement gaps; no idle polling.
            if((retry||!File.Exists(globalFile)||(scope!=null&&!File.Exists(file)))&&readRetries<5){readRetries++;readTimer.Change(100,Timeout.Infinite);}
        }if(changed)Raise();}
        void QueueSave(ThemeProfile p){pending=p.Clone();writeRetries=0;if(!writeScheduled){writeScheduled=true;saveTimer.Change(100,Timeout.Infinite);}}
        public void SetScopeOverrideEnabled(bool enabled){lock(gate){if(disposed)throw new ObjectDisposedException("ThemeStore");if(scope==null)throw new InvalidOperationException("HUB has no inherited scope");if(enabled&&scoped.Profile==null){scoped.Profile=current.Clone();scoped.Profile.Mode="Custom";}scoped.Enabled=enabled;QueueSave(scoped.Profile??current);}Raise();}
        public void Save(ThemeProfile profile){if(profile==null)throw new ArgumentNullException("profile");ThemeProfile p=profile.Clone();p.Normalize();lock(gate){if(disposed)throw new ObjectDisposedException("ThemeStore");if(scope==null)current=p;else{p.Mode="Custom";scoped.Profile=p;scoped.Enabled=true;}QueueSave(p);}Raise();}
        void WritePending(object state) {
            bool notify=false;
            lock(gate){
                if(disposed||pending==null)return;writeScheduled=false;
                try{FlushPending();if(lastError!=null){lastError=null;notify=true;}writeRetries=0;}
                catch(IOException e){notify=RecordWriteFailure(e);}
                catch(UnauthorizedAccessException e){notify=RecordWriteFailure(e);}
            }
            if(notify)Raise();
        }
        bool RecordWriteFailure(Exception error) {
            bool notify=lastError!=error.Message;lastError=error.Message;
            // At most five one-shot retries after the initial attempt. New user edits reset the budget.
            if(writeRetries<5){writeRetries++;writeScheduled=true;saveTimer.Change(100,Timeout.Infinite);}
            return notify;
        }
        void FlushPending() {
            if(pending==null)return;string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(scope==null?(object)pending:scoped));if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);pending=null;}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public string ImportBackground(string source) {
            lock(gate){if(disposed)throw new ObjectDisposedException("ThemeStore");}
            var info=new FileInfo(source);if(!info.Exists||info.Length==0||info.Length>20*1024*1024)throw new InvalidDataException("Image must be between 1 byte and 20 MiB.");
            byte[] bytes;using(var input=new FileStream(info.FullName,FileMode.Open,FileAccess.Read,FileShare.Read)){if(input.Length==0||input.Length>20*1024*1024)throw new InvalidDataException("Image exceeds 20 MiB.");bytes=new byte[(int)input.Length];int offset=0;while(offset<bytes.Length){int count=input.Read(bytes,offset,bytes.Length-offset);if(count==0)throw new EndOfStreamException();offset+=count;}}
            using(var stream=new MemoryStream(bytes,false)){BitmapDecoder decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.None);if(decoder.Frames.Count!=1)throw new InvalidDataException("Use a single-frame image.");var frame=decoder.Frames[0];if(frame.PixelWidth>8192||frame.PixelHeight>8192||(long)frame.PixelWidth*frame.PixelHeight>32000000)throw new InvalidDataException("Image dimensions exceed the supported limit.");}
            string assets=Path.Combine(root,"assets");Directory.CreateDirectory(assets);string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();string dest=Path.Combine(assets,hash+".img");if(!File.Exists(dest)){string temp=dest+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,bytes);try{File.Move(temp,dest);}catch(IOException){if(!File.Exists(dest))throw;}}finally{if(File.Exists(temp))File.Delete(temp);}}return dest;
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;saveTimer.Dispose();readTimer.Dispose();watcher.EnableRaisingEvents=false;watcher.Dispose();SystemEvents.UserPreferenceChanged-=OnPreference;AppDomain.CurrentDomain.AssemblyLoad-=OnAssemblyLoad;if(systemSubscribed)DetachSystemProperties();FlushPending();}}
    }
}
