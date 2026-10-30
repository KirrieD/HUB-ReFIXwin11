using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Input;

namespace ReFix.Shared
{
    public sealed class ControlAction
    {
        public string Id { get; private set; } public string Name { get; private set; }
        public string Scope { get; private set; } public string DefaultGesture { get; private set; }
        public string Description { get; private set; }
        internal ControlAction(string id,string name,string scope,string gesture,string description) { Id=id;Name=name;Scope=scope;DefaultGesture=gesture;Description=description; }
    }
    public static class ControlActions
    {
        public static readonly ReadOnlyCollection<ControlAction> All=Array.AsReadOnly(new[] {
            new ControlAction("hub.toggle","Показать / скрыть HUB","global","Ctrl+Alt+H","Открыть или скрыть главное окно"),
            new ControlAction("quiet-mixer.toggle","Показать / скрыть микшер","global","Ctrl+Alt+V","Открыть или скрыть микшер"),
            new ControlAction("mini-icons.toggle","Показать / скрыть мини значки","global","Ctrl+Alt+I","Открыть или скрыть мини-меню"),
            new ControlAction("task-menu.toggle","Показать / скрыть меню","global","Ctrl+Alt+M","Открыть или скрыть меню задач"),
            new ControlAction("quick-note.toggle","Показать / скрыть заметки","global","","Открыть или скрыть заметки"),
            new ControlAction("hub.hide","Скрыть HUB","hub","Esc","Скрыть главное окно"),
            new ControlAction("quiet-mixer.hide","Скрыть микшер","quiet-mixer","Esc","Скрыть микшер"),
            new ControlAction("mini-icons.hide","Скрыть мини значки","mini-icons","Esc","Скрыть мини-меню"),
            new ControlAction("task-menu.hide","Скрыть меню","task-menu","Esc","Скрыть меню задач"),
            new ControlAction("quick-note.hide","Скрыть заметки","quick-note","Esc","Скрыть заметки"),
            new ControlAction("quick-note.save","Сохранить заметку","quick-note","Ctrl+S","Сохранить текущую заметку") });
        internal static ControlAction Find(string id) { foreach(var a in All)if(a.Id==id)return a;throw new ArgumentException("Unknown control action: "+id); }
    }
    public sealed class ControlsProfile
    {
        public bool AutoHideMiniIcons {get;set;} public bool StartMiniIconsWithHub {get;set;}
        public TaskMenuLayer TaskMenuLayer { get; set; } public bool ShowTaskMenuEdges { get; set; }
        public Dictionary<string,string> Bindings { get; set; } public bool StartMixerWithHub { get; set; } public bool AutoHideHub { get; set; } public bool AutoHideMixer { get; set; } public bool AutoHideTaskMenu { get; set; } public bool StartTaskMenuWithHub { get; set; }
        public ControlsProfile() { AutoHideMiniIcons=true; ShowTaskMenuEdges=true;AutoHideHub=true;AutoHideMixer=true;AutoHideTaskMenu=true;Bindings=new Dictionary<string,string>();foreach(var a in ControlActions.All)Bindings[a.Id]=a.DefaultGesture; }
        public string Get(string id) { var a=ControlActions.Find(id);string v;return Bindings!=null&&Bindings.TryGetValue(id,out v)?v??"":a.DefaultGesture; }
        public ControlsProfile Clone() { var p=new ControlsProfile();p.AutoHideMiniIcons=AutoHideMiniIcons;p.StartMiniIconsWithHub=StartMiniIconsWithHub;p.TaskMenuLayer=TaskMenuLayerPolicy.Normalize(TaskMenuLayer);p.ShowTaskMenuEdges=ShowTaskMenuEdges;p.StartMixerWithHub=StartMixerWithHub;p.AutoHideHub=AutoHideHub;p.AutoHideMixer=AutoHideMixer;p.AutoHideTaskMenu=AutoHideTaskMenu;p.StartTaskMenuWithHub=StartTaskMenuWithHub;if(Bindings!=null)foreach(var b in Bindings)p.Bindings[b.Key]=b.Value;return p; }
    }
    public sealed class KeyChord
    {
        public string Gesture { get; private set; } public uint Modifiers { get; private set; } public uint VirtualKey { get; private set; }
        KeyChord(string gesture,uint modifiers,uint vk) {Gesture=gesture;Modifiers=modifiers;VirtualKey=vk;}
        public static KeyChord Parse(string text)
        {
            if(String.IsNullOrWhiteSpace(text))return new KeyChord("",0,0);
            uint mods=0;Key key=Key.None;string[] parts=text.Split('+');
            foreach(string raw in parts) {
                string part=raw.Trim();uint flag=0;
                switch(part.ToUpperInvariant()){case "CTRL":case "CONTROL":flag=2;break;case "ALT":flag=1;break;case "SHIFT":flag=4;break;case "WIN":case "WINDOWS":flag=8;break;}
                if(flag!=0){if((mods&flag)!=0)throw new ArgumentException("Repeated modifier");mods|=flag;continue;}
                if(key!=Key.None)throw new ArgumentException("Use exactly one physical key");
                if(part.Equals("Esc",StringComparison.OrdinalIgnoreCase))part="Escape";
                if(part.Equals("Enter",StringComparison.OrdinalIgnoreCase))part="Return"; if(part.Equals("Backspace",StringComparison.OrdinalIgnoreCase))part="Back";
                if(part.Length==1&&part[0]>='0'&&part[0]<='9')part="D"+part;
                if(!Enum.TryParse<Key>(part,true,out key)||!Enum.IsDefined(typeof(Key),key))throw new ArgumentException("Unknown physical key: "+part);
                bool accepted=(key>=Key.A&&key<=Key.Z)||(key>=Key.D0&&key<=Key.D9)||(key>=Key.F1&&key<=Key.F24)||key==Key.Escape||key==Key.Return||key==Key.Space||key==Key.Tab||key==Key.Back||key==Key.Delete||key==Key.Insert||key==Key.Home||key==Key.End||key==Key.PageUp||key==Key.PageDown||key==Key.Left||key==Key.Right||key==Key.Up||key==Key.Down;
                if(!accepted)throw new ArgumentException("Unsupported physical key");
            }
            if(key==Key.None)throw new ArgumentException("A physical key is required");
            uint vk=(uint)KeyInterop.VirtualKeyFromKey(key);
            if(key==Key.F12||(mods&8)!=0||(mods==1&&(key==Key.Tab||key==Key.Escape||key==Key.F4))||(mods==2&&key==Key.Escape)||(mods==3&&key==Key.Delete))throw new ArgumentException("This shortcut is reserved by Windows");
            string name=key==Key.Escape?"Esc":key==Key.Return?"Enter":key==Key.Back?"Backspace":key==Key.PageUp?"PageUp":key==Key.PageDown?"PageDown":key.ToString();
            if(key>=Key.D0&&key<=Key.D9)name=((int)(key-Key.D0)).ToString();
            return new KeyChord(((mods&2)!=0?"Ctrl+":"")+((mods&1)!=0?"Alt+":"")+((mods&4)!=0?"Shift+":"")+((mods&8)!=0?"Win+":"")+name,mods,vk);
        }
        public bool Matches(Key key,ModifierKeys modifiers) { if(key==Key.System||key==Key.ImeProcessed)return false;uint m=((modifiers&ModifierKeys.Alt)!=0?1u:0)|((modifiers&ModifierKeys.Control)!=0?2u:0)|((modifiers&ModifierKeys.Shift)!=0?4u:0)|((modifiers&ModifierKeys.Windows)!=0?8u:0);return VirtualKey!=0&&m==Modifiers&&(uint)KeyInterop.VirtualKeyFromKey(key)==VirtualKey; }
    }
    public sealed class HotkeyRuntimeStatus
    {
        public string ActionId { get; set; } public string Requested { get; set; } public string Actual { get; set; } public string Error { get; set; }
        public int ProcessId { get; set; } public long ProcessStartTicks { get; set; }
    }
    internal static class ControlsNative
    {
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
        [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(IntPtr hwnd,int id);
        [DllImport("user32.dll",SetLastError=true)] internal static extern bool PostMessage(IntPtr hwnd,int msg,IntPtr wParam,IntPtr lParam);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string text);
        internal static bool Alive(HotkeyRuntimeStatus s) {try{using(var p=Process.GetProcessById(s.ProcessId))return !p.HasExited&&p.StartTime.ToUniversalTime().Ticks==s.ProcessStartTicks;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(Win32Exception){return false;} }
    }
    public sealed class ControlsStore : IDisposable
    {
        readonly object gate=new object();readonly string file,runtime;readonly FileSystemWatcher watcher,runtimeWatcher;readonly Timer reloadTimer;ControlsProfile current;bool disposed;int reloadAttempts;
        public event EventHandler Changed;public event EventHandler RuntimeChanged;
        public ControlsProfile Current {get{lock(gate)return current.Clone();}}
        public ControlsStore(string root) {
            if(String.IsNullOrWhiteSpace(root))throw new ArgumentException("A data root is required");Directory.CreateDirectory(root);file=Path.Combine(Path.GetFullPath(root),"controls.json");runtime=Path.Combine(Path.GetFullPath(root),"controls-runtime");Directory.CreateDirectory(runtime);
            current=Read()??new ControlsProfile();reloadTimer=new Timer(ReloadConfig,null,Timeout.Infinite,Timeout.Infinite);watcher=new FileSystemWatcher(Path.GetDirectoryName(file),"controls.json");watcher.NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size;watcher.Changed+=OnConfig;watcher.Created+=OnConfig;watcher.Renamed+=OnConfig;watcher.Deleted+=OnConfig;watcher.EnableRaisingEvents=true;
            runtimeWatcher=new FileSystemWatcher(runtime,"*.json");runtimeWatcher.NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size;runtimeWatcher.Changed+=OnRuntime;runtimeWatcher.Created+=OnRuntime;runtimeWatcher.Deleted+=OnRuntime;runtimeWatcher.Renamed+=OnRuntime;runtimeWatcher.EnableRaisingEvents=true;
        }
        internal static T ReadJson<T>(string path) where T:class {try{using(var s=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)){if(s.Length>65536)return null;using(var r=new StreamReader(s)){var chars=new char[65537];int n=r.ReadBlock(chars,0,chars.Length);return n>65536?null:new JavaScriptSerializer().Deserialize<T>(new string(chars,0,n));}}}catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;} }
        ControlsProfile Read(){var p=ReadJson<ControlsProfile>(file);if(p==null)return null;try{return Validate(p);}catch(ArgumentException){return null;}}
        static ControlsProfile Validate(ControlsProfile profile) {
            var p=profile.Clone();foreach(var b in p.Bindings)ControlActions.Find(b.Key);
            // A newly introduced default must not invalidate a user's older saved bindings.
            if(profile.Bindings!=null&&!profile.Bindings.ContainsKey("mini-icons.toggle"))foreach(var b in p.Bindings)if(b.Key!="mini-icons.toggle"&&KeyChord.Parse(b.Value).Gesture=="Ctrl+Alt+I"){p.Bindings["mini-icons.toggle"]="";break;}
            foreach(var a in ControlActions.All){var c=KeyChord.Parse(p.Get(a.Id));if(c.VirtualKey!=0&&a.Scope=="global"&&(c.Modifiers&3)==0)throw new ArgumentException("Global shortcuts require Ctrl or Alt");p.Bindings[a.Id]=c.Gesture;}
            for(int i=0;i<ControlActions.All.Count;i++)for(int j=i+1;j<ControlActions.All.Count;j++){var a=ControlActions.All[i];var b=ControlActions.All[j];if(p.Get(a.Id)!=""&&p.Get(a.Id)==p.Get(b.Id)&&(a.Scope==b.Scope||a.Scope=="global"||b.Scope=="global"))throw new ArgumentException("Shortcut conflicts: "+a.Name+" / "+b.Name);}
            return p;
        }
        void Raise(EventHandler h){if(h!=null)ThreadPool.QueueUserWorkItem(delegate{lock(gate){if(disposed)return;}h(this,EventArgs.Empty);});}
        // File.Replace raises rename notifications while its destination can still be
        // missing or locked. Coalesce the burst and retry transient reads, otherwise
        // a successfully saved edit can be lost until the process is restarted.
        void OnConfig(object sender,FileSystemEventArgs e){lock(gate){if(disposed)return;reloadAttempts=0;reloadTimer.Change(40,Timeout.Infinite);}}
        void ReloadConfig(object state){lock(gate){if(disposed)return;var next=Read();if(next==null){if(++reloadAttempts<6)reloadTimer.Change(40,Timeout.Infinite);return;}if(new JavaScriptSerializer().Serialize(next)==new JavaScriptSerializer().Serialize(current))return;current=next;}Raise(Changed);}
        void OnRuntime(object sender,FileSystemEventArgs e){Raise(RuntimeChanged);}
        public void Assign(string actionId,string gesture){ControlActions.Find(actionId);var p=Current;p.Bindings[actionId]=gesture;Save(p);}
        public void Save(ControlsProfile profile){if(profile==null)throw new ArgumentNullException("profile");var p=Validate(profile);lock(gate){if(disposed)throw new ObjectDisposedException("ControlsStore");foreach(var a in ControlActions.All)if(a.Scope=="global"&&p.Get(a.Id)!=""&&p.Get(a.Id)!=current.Get(a.Id))Probe(a.Id,p.Get(a.Id));WriteAtomic(file,p);current=p;}Raise(Changed);}
        void Probe(string action,string gesture){var live=GetRuntime(action);if(live!=null&&live.Actual==gesture)return;var c=KeyChord.Parse(gesture);Exception error=null;var t=new Thread(delegate(){if(!ControlsNative.RegisterHotKey(IntPtr.Zero,0x3FF0,c.Modifiers|0x4000,c.VirtualKey)){error=new InvalidOperationException("Shortcut is unavailable in Windows: "+gesture);return;}ControlsNative.UnregisterHotKey(IntPtr.Zero,0x3FF0);});t.Start();t.Join();if(error!=null)throw error;}
        public void ResetDefaults(){var defaults=new ControlsProfile();var previous=Current;defaults.AutoHideMiniIcons=previous.AutoHideMiniIcons;defaults.StartMiniIconsWithHub=previous.StartMiniIconsWithHub;defaults.TaskMenuLayer=previous.TaskMenuLayer;defaults.ShowTaskMenuEdges=previous.ShowTaskMenuEdges;defaults.StartMixerWithHub=previous.StartMixerWithHub;defaults.AutoHideHub=previous.AutoHideHub;defaults.AutoHideMixer=previous.AutoHideMixer;defaults.AutoHideTaskMenu=previous.AutoHideTaskMenu;defaults.StartTaskMenuWithHub=previous.StartTaskMenuWithHub;Save(defaults);}
        internal static void WriteAtomic(string path,object value){string tmp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(tmp,new JavaScriptSerializer().Serialize(value));if(File.Exists(path))File.Replace(tmp,path,null);else File.Move(tmp,path);}finally{if(File.Exists(tmp))File.Delete(tmp);}}
        internal string RuntimePath(string id,int pid,long ticks){return Path.Combine(runtime,id+"."+pid+"."+ticks+".json");}
        public HotkeyRuntimeStatus GetRuntime(string actionId){ControlActions.Find(actionId);HotkeyRuntimeStatus found=null;foreach(string path in Directory.GetFiles(runtime,actionId+".*.json")){var s=ReadJson<HotkeyRuntimeStatus>(path);if(s==null||s.ActionId!=actionId||!ControlsNative.Alive(s))continue;if(found==null||s.ProcessStartTicks>found.ProcessStartTicks)found=s;}return found;}
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;}watcher.Dispose();runtimeWatcher.Dispose();reloadTimer.Dispose();}
    }
    public sealed class GlobalKeyRegistration : IDisposable
    {
        readonly ControlsStore store;readonly string actionId,path;readonly IntPtr hwnd;readonly int baseId,rebind;readonly Action pressed;readonly int pid;readonly long ticks;int activeId;string actual="";bool disposed;
        public GlobalKeyRegistration(ControlsStore store,string actionId,IntPtr hwnd,int baseId,Action pressed){if(store==null)throw new ArgumentNullException("store");if(ControlActions.Find(actionId).Scope!="global")throw new ArgumentException("Action is not global");if(hwnd==IntPtr.Zero)throw new ArgumentException("An owning window handle is required");if(baseId<1||baseId>=0xBFFF)throw new ArgumentOutOfRangeException("baseId");this.store=store;this.actionId=actionId;this.hwnd=hwnd;this.baseId=baseId;this.pressed=pressed;using(var p=Process.GetCurrentProcess()){pid=p.Id;ticks=p.StartTime.ToUniversalTime().Ticks;}path=store.RuntimePath(actionId,pid,ticks);rebind=(int)ControlsNative.RegisterWindowMessage("ReFix.Controls.Rebind."+actionId+"."+baseId);store.Changed+=OnChanged;store.RuntimeChanged+=OnRuntimeChanged;Rebind();}
        void OnChanged(object sender,EventArgs e){if(!disposed)ControlsNative.PostMessage(hwnd,rebind,new IntPtr(baseId),IntPtr.Zero);}
        void OnRuntimeChanged(object sender,EventArgs e){if(!disposed&&store.Current.Get(actionId)!=actual)OnChanged(sender,e);}
        void Rebind(){if(disposed)return;string requested=store.Current.Get(actionId),error=null;if(requested!=actual){if(requested==""){if(activeId!=0)ControlsNative.UnregisterHotKey(hwnd,activeId);activeId=0;actual="";}else{var c=KeyChord.Parse(requested);int next=activeId==baseId?baseId+1:baseId;if(ControlsNative.RegisterHotKey(hwnd,next,c.Modifiers|0x4000,c.VirtualKey)){if(activeId!=0)ControlsNative.UnregisterHotKey(hwnd,activeId);activeId=next;actual=requested;}else error="Windows could not register shortcut ("+Marshal.GetLastWin32Error()+")";}}try{var status=new HotkeyRuntimeStatus{ActionId=actionId,Requested=requested,Actual=actual,Error=error,ProcessId=pid,ProcessStartTicks=ticks};var previous=ControlsStore.ReadJson<HotkeyRuntimeStatus>(path);if(previous==null||previous.Requested!=requested||previous.Actual!=actual||previous.Error!=error)ControlsStore.WriteAtomic(path,status);}catch(IOException){}catch(UnauthorizedAccessException){} }
        public bool ProcessMessage(int msg,IntPtr wParam){if(disposed)return false;if(msg==rebind&&wParam.ToInt32()==baseId){Rebind();return true;}if(msg==0x0312&&activeId!=0&&wParam.ToInt32()==activeId){if(pressed!=null)pressed();return true;}return false;}
        public void Dispose(){if(disposed)return;disposed=true;store.Changed-=OnChanged;store.RuntimeChanged-=OnRuntimeChanged;if(activeId!=0)ControlsNative.UnregisterHotKey(hwnd,activeId);try{var s=ControlsStore.ReadJson<HotkeyRuntimeStatus>(path);if(s!=null&&s.ProcessId==pid&&s.ProcessStartTicks==ticks)File.Delete(path);}catch(IOException){}catch(UnauthorizedAccessException){} }
    }
}
