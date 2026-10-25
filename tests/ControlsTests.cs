using System;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using System.Runtime.InteropServices;
using ReFix.Shared;
namespace ReFix.Tests {
public static class ControlsTests {
public static void Run(Action<string,bool> report) {
Type t=typeof(KeyChord);report("keyboard parser available",t!=null);if(t==null)return;
object c=t.GetMethod("Parse").Invoke(null,new object[]{"shift+ctrl+f22"});report("canonical physical chord",(string)t.GetProperty("Gesture").GetValue(c,null)=="Ctrl+Shift+F22");
report("canonical Backspace parses again",KeyChord.Parse("Backspace").Gesture=="Backspace");
report("local keys match exact modifiers",KeyChord.Parse("Ctrl+S").Matches(Key.S,ModifierKeys.Control)&&!KeyChord.Parse("Ctrl+S").Matches(Key.S,ModifierKeys.None));
report("disabled chord never matches",!KeyChord.Parse("").Matches(Key.None,ModifierKeys.None));
report("reserved OS chord rejected",Reject(delegate{KeyChord.Parse("Alt+Tab");}));
string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"refix-controls-test-"+Guid.NewGuid().ToString("N"));
try { using(var store=new ControlsStore(root)) {
report("contextual Escape may repeat",store.Current.Get("hub.hide")=="Esc"&&store.Current.Get("quick-note.hide")=="Esc");
report("global cannot shadow local",Reject(delegate{store.Assign("quick-note.toggle","Ctrl+S");}));
report("global requires Ctrl or Alt",Reject(delegate{store.Assign("quick-note.toggle","Shift+F22");}));
report("local scope conflict rejected",Reject(delegate{store.Assign("quick-note.save","Esc");}));
var p=store.Current;p.Bindings["quick-note.save"]="Ctrl+Shift+S";store.Save(p);p.Bindings["quick-note.save"]="Esc";
report("saved profile and snapshots are isolated",store.Current.Get("quick-note.save")=="Ctrl+Shift+S");
var changed=new ManualResetEvent(false);int caller=Thread.CurrentThread.ManagedThreadId;bool background=false;
store.Changed+=delegate{background=Thread.CurrentThread.ManagedThreadId!=caller;changed.Set();};
File.WriteAllText(Path.Combine(root,"controls.json"),"{invalid");Thread.Sleep(150);report("invalid external config retains last good",store.Current.Get("quick-note.save")=="Ctrl+Shift+S");
changed.Reset();var external=new ControlsProfile();external.Bindings["quick-note.save"]="Alt+S";File.WriteAllText(Path.Combine(root,"controls.json"),new JavaScriptSerializer().Serialize(external));
report("external config reloads without polling",changed.WaitOne(3000)&&store.Current.Get("quick-note.save")=="Alt+S"&&background);
File.WriteAllText(Path.Combine(root,"controls-runtime","hub.toggle.stale.json"),new JavaScriptSerializer().Serialize(new HotkeyRuntimeStatus{ActionId="hub.toggle",Actual="Ctrl+Alt+H",ProcessId=System.Diagnostics.Process.GetCurrentProcess().Id,ProcessStartTicks=1}));
report("runtime PID reuse is not shown active",store.GetRuntime("hub.toggle")==null);
var startup=store.Current;startup.StartMixerWithHub=true;store.Save(startup);store.ResetDefaults();
report("resetting keys preserves mixer startup preference",store.Current.StartMixerWithHub&&store.Current.Get("quick-note.save")=="Ctrl+S");
NativeLifecycle(store,root,report);
changed.Dispose();
} } finally { if(Directory.Exists(root))Directory.Delete(root,true); }
}
static bool Reject(Action a){try{a();return false;}catch(ArgumentException){return true;}catch(InvalidOperationException){return true;}}
[DllImport("user32.dll")] static extern bool RegisterHotKey(IntPtr hwnd,int id,uint modifiers,uint key);
[DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr hwnd,int id);
static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);}
static bool Wait(Func<bool> check){DateTime until=DateTime.UtcNow.AddSeconds(4);do{Pump();if(check())return true;Thread.Sleep(10);}while(DateTime.UtcNow<until);return false;}
static void NativeLifecycle(ControlsStore store,string root,Action<string,bool> report){
store.Assign("quick-note.toggle","Ctrl+Alt+Shift+F22");
using(var hwnd=new HwndSource(new HwndSourceParameters("ReFix isolated key tests"){Width=1,Height=1,WindowStyle=0})){
int presses=0;using(var registration=new GlobalKeyRegistration(store,"quick-note.toggle",hwnd.Handle,0x6A20,delegate{presses++;})){
hwnd.AddHook(delegate(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){handled=registration.ProcessMessage(msg,w);return IntPtr.Zero;});
report("native registration reports live identity",store.GetRuntime("quick-note.toggle").Actual=="Ctrl+Alt+Shift+F22");
registration.ProcessMessage(0x0312,new IntPtr(0x6A20));report("owned hotkey dispatch invokes action",presses==1);
var ready=new ManualResetEvent(false);var release=new ManualResetEvent(false);bool held=false;
var owner=new Thread(delegate(){held=RegisterHotKey(IntPtr.Zero,0x6A30,7,0x86);ready.Set();release.WaitOne();if(held)UnregisterHotKey(IntPtr.Zero,0x6A30);});owner.Start();ready.WaitOne();
try{report("isolated conflict key reserved",held);if(held){
report("unavailable global refused before persistence",Reject(delegate{store.Assign("quick-note.toggle","Ctrl+Alt+Shift+F23");})&&store.Current.Get("quick-note.toggle")=="Ctrl+Alt+Shift+F22");
var p=store.Current;p.Bindings["quick-note.toggle"]="Ctrl+Alt+Shift+F23";File.WriteAllText(Path.Combine(root,"controls.json"),new JavaScriptSerializer().Serialize(p));
report("late conflict preserves actual old key",Wait(delegate{var s=store.GetRuntime("quick-note.toggle");return s!=null&&s.Requested=="Ctrl+Alt+Shift+F23"&&s.Actual=="Ctrl+Alt+Shift+F22"&&!String.IsNullOrEmpty(s.Error);}));
}}finally{release.Set();owner.Join();ready.Dispose();release.Dispose();}
store.Save(store.Current);report("explicit retry acquires released key",Wait(delegate{var s=store.GetRuntime("quick-note.toggle");return s!=null&&s.Actual=="Ctrl+Alt+Shift+F23"&&s.Error==null;}));
store.Assign("quick-note.toggle","");report("disable unregisters live key",Wait(delegate{var s=store.GetRuntime("quick-note.toggle");return s!=null&&s.Actual=="";}));
}report("dispose removes owned runtime status",store.GetRuntime("quick-note.toggle")==null);
}}
#if CONTROLS_STANDALONE
[STAThread] public static int Main(){int f=0;Run(delegate(string n,bool ok){Console.WriteLine((ok?"PASS ":"FAIL ")+n);if(!ok)f++;});return f==0?0:1;}
#endif
}}
