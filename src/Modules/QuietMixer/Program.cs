using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using Microsoft.Win32;
using ReFix.Shared;
using Forms = System.Windows.Forms;

namespace QuietMixer
{
    internal static class Diagnostics
    {
        private static DateTime lastLog=DateTime.MinValue;
        internal static void Log(Exception ex)
        {
            // Avoid a repeating device failure growing a log once a second.
            if((DateTime.UtcNow-lastLog).TotalMinutes<1) return; lastLog=DateTime.UtcNow;
            try
            {
                string path=Path.Combine(Paths.Root,"logs","quiet-mixer");
                Directory.CreateDirectory(path);
                string file=Path.Combine(path,"error.log");
                if(File.Exists(file) && new FileInfo(file).Length>65536) File.Delete(file);
                File.AppendAllText(file,DateTime.Now.ToString("s")+" "+ex+Environment.NewLine);
            }
            catch { }
        }
        internal static void Write(string path)
        {
            using (StreamWriter writer=new StreamWriter(path,false,Encoding.UTF8))
            {
                writer.WriteLine("QuietMixer 1.0 | "+Environment.OSVersion+" | "+DateTime.Now.ToString("s"));
                try { ShellSnapshot snapshot=ShellListener.Discover(writer); writer.WriteLine("SPEAKERS: "+snapshot.Speakers.Length); }
                catch(Exception ex) { writer.WriteLine("SHELL ERROR: "+ex); }
                try
                {
                    using(AudioEngine audio=new AudioEngine())
                    {
                        writer.WriteLine("AUDIO: "+audio.DeviceId+" volume="+audio.Master.Volume+" mute="+audio.Master.Muted);
                        var sessions=audio.Sessions();
                        foreach(AudioChannel channel in sessions) { try { writer.WriteLine("SESSION: "+channel.Name+" volume="+channel.Volume+" mute="+channel.Muted); } finally { channel.Dispose(); } }
                    }
                }
                catch(Exception ex) { writer.WriteLine("AUDIO ERROR: "+ex); }
            }
        }
    }
    internal static class Program
    {
        private static readonly string ListenerName=ModuleRuntime.PipeName("quiet-mixer")+".Listener";
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                if(args.Length>=2 && args[0]=="--diagnose") { Diagnostics.Write(args[1]); return; }
                if(args.Length>0 && args[0]=="--exit") { Native.PostMessage(Native.FindWindow(null,ListenerName),Native.ExitMessage,IntPtr.Zero,IntPtr.Zero); return; }
                bool created;
                using(Mutex mutex=new Mutex(true,"Local\\"+ModuleRuntime.PipeName("quiet-mixer"),out created))
                {
                    if(!created) { Native.PostMessage(Native.FindWindow(null,ListenerName),Native.ToggleMessage,IntPtr.Zero,IntPtr.Zero); return; }
                    Run(args);
                }
            }
            catch(Exception ex) { Diagnostics.Log(ex); MessageBox.Show("Не удалось запустить QuietMixer.\n\n"+ex.Message,"QuietMixer",MessageBoxButton.OK,MessageBoxImage.Error); }
        }
        private static void Run(string[] args)
        {
            Application app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException+=delegate(object sender,System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e) { Diagnostics.Log(e.Exception); e.Handled=true; app.Shutdown(1); };
            MixerWindow popup=null;
            HwndSourceParameters parameters=new HwndSourceParameters(ListenerName) { Width=0,Height=0,WindowStyle=unchecked((int)0x80000000) };
            using(HwndSource source=new HwndSource(parameters))
            using(ShellListener listener=new ShellListener(source.Handle))
            using(Forms.NotifyIcon tray=new Forms.NotifyIcon())
            using(ThemeStore themes=new ThemeStore(Paths.Root))
            {
                Action show=delegate { if(popup==null) popup=new MixerWindow(themes.Current); popup.ShowAtCursor(); };
                Action toggle=delegate { if(popup!=null && popup.IsVisible) popup.Hide(); else show(); };
                uint taskbarCreated=Native.RegisterWindowMessage("TaskbarCreated");
                source.AddHook(delegate(IntPtr hwnd,int msg,IntPtr wp,IntPtr lp,ref bool handled)
                {
                    if(msg==Native.ToggleMessage || msg==0x312) { toggle(); handled=true; }
                    if(msg==Native.ExitMessage) { app.Shutdown(); handled=true; }
                    if(msg==0x7E || msg==0x1A) listener.Refresh();
                    if((uint)msg==taskbarCreated) { tray.Visible=false; tray.Visible=true; listener.Refresh(); }
                    return IntPtr.Zero;
                });
                bool hotkey=Native.RegisterHotKey(source.Handle,1,0x4003,0x56);
                tray.Icon=System.Drawing.SystemIcons.Application;
                tray.Text="QuietMixer · Ctrl+Alt+V";
                Forms.ContextMenuStrip menu=new Forms.ContextMenuStrip();
                menu.Items.Add("Открыть микшер",null,delegate { show(); });
                menu.Items.Add("Выйти",null,delegate { app.Shutdown(); });
                tray.ContextMenuStrip=menu; tray.MouseClick+=delegate(object sender,Forms.MouseEventArgs e) { if(e.Button==Forms.MouseButtons.Left) toggle(); };
                tray.Visible=true;
                app.Exit+=delegate { tray.Visible=false; if(hotkey) Native.UnregisterHotKey(source.Handle,1); if(popup!=null) popup.Close(); menu.Dispose(); };
                EventHandler changed=delegate { app.Dispatcher.BeginInvoke(new Action(delegate { if(popup!=null) popup.ApplyTheme(themes.Current); })); };
                themes.Changed+=changed;
                using(ModuleRuntime runtime=new ModuleRuntime("quiet-mixer",
                    delegate { app.Dispatcher.BeginInvoke(show); },
                    delegate { app.Dispatcher.BeginInvoke(new Action(delegate { app.Shutdown(); })); }))
                {
                runtime.Start();
                if(Array.IndexOf(args,"--background")<0) app.Dispatcher.BeginInvoke(show, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                if(!listener.HookAvailable || !hotkey)
                {
                    tray.ShowBalloonTip(6000,"QuietMixer",!listener.HookAvailable?"Не удалось включить перехват динамика. Открывайте микшер через этот значок.":"Ctrl+Alt+V занято другой программой. Микшер доступен через значок в трее.",Forms.ToolTipIcon.Info);
                }
                app.Run();
                themes.Changed-=changed;
                }
            }
        }
    }
}
