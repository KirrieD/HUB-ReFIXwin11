using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using ReFix.Shared;
using Forms=System.Windows.Forms;

namespace ReFix.Hub
{
    internal static class Startup
    {
        private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        internal static bool Enabled
        {
            get { try{using(RegistryKey key=Registry.CurrentUser.OpenSubKey(RunKey))return key!=null && key.GetValue("ReFIXHub")!=null;}catch(System.Security.SecurityException){return false;}catch(UnauthorizedAccessException){return false;} }
            set { using(RegistryKey key=Registry.CurrentUser.CreateSubKey(RunKey)) { if(value)key.SetValue("ReFIXHub","\""+Assembly.GetExecutingAssembly().Location+"\" --background");else key.DeleteValue("ReFIXHub",false); } }
        }
    }
    internal interface IHubSession
    {
        bool IsVisible {get;}
        void Show();void Toggle();void Hide();void OpenStyles();void Render(string path);void Close();
    }
    internal static class Program
    {
        internal const int Toggle=0x8101,Quit=0x8102;
        private static string Instance
        {
            get { using(SHA256 hash=SHA256.Create())return "ReFIXHub."+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User.Value+Paths.Root))).Replace("-","").Substring(0,20); }
        }
        internal static void Log(Exception ex){try{Paths.Ensure();File.WriteAllText(Path.Combine(Paths.Root,"hub-error.log"),ex.ToString());}catch{}}
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                if(args.Contains("--exit")){Native.PostMessage(Native.FindWindow(null,Instance),Quit,IntPtr.Zero,IntPtr.Zero);return;}
                bool created;
                using(Mutex mutex=new Mutex(true,"Local\\"+Instance,out created))
                {
                    if(!created){if(!args.Contains("--background"))Native.PostMessage(Native.FindWindow(null,Instance),Toggle,IntPtr.Zero,IntPtr.Zero);return;}
                    Run(args);
                }
            }
            catch(Exception ex){Log(ex);if(!args.Contains("--background"))Forms.MessageBox.Show("Не удалось открыть HUB ReFIX.\n\n"+ex.Message,"HUB ReFIX",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Error);}
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IHubSession OpenSession(ThemeStore themes,ModuleManager modules){return new HubSession(themes,modules);}
        private static void Run(string[] args)
        {
            Paths.Ensure();
            using(ThemeStore themes=new ThemeStore(Paths.Root))
            using(ModuleManager modules=new ModuleManager(Paths.Root,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog.json")))
            using(TrayListener listener=new TrayListener(Instance))
            using(Forms.NotifyIcon tray=new Forms.NotifyIcon())
            using(Forms.ContextMenuStrip menu=new Forms.ContextMenuStrip())
            {
                IHubSession session=null;
                Action show=delegate
                {
                    try{if(session==null)session=OpenSession(themes,modules);session.Show();}
                    catch(Exception ex){Log(ex);Forms.MessageBox.Show("Не удалось открыть интерфейс HUB.\n\n"+ex.Message+"\n\nЗапустите HUB повторно.","HUB ReFIX",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Error);Forms.Application.ExitThread();}
                };
                Action toggle=delegate{if(session==null)show();else session.Toggle();};
                uint recreated=Native.RegisterWindowMessage("TaskbarCreated");
                listener.Message+=delegate(int message)
                {
                    if(message==Toggle || message==0x312)toggle();
                    else if(message==Quit)Forms.Application.ExitThread();
                    else if((uint)message==recreated){tray.Visible=false;tray.Visible=true;}
                };
                bool hotkey=Native.RegisterHotKey(listener.Handle,1,0x4003,0x48);
                menu.Items.Add("Открыть HUB",null,delegate{show();});
                menu.Items.Add("Оформление",null,delegate{show();session.OpenStyles();});
                menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Выйти",null,delegate{Forms.Application.ExitThread();});
                tray.Icon=System.Drawing.SystemIcons.Shield;tray.Text="HUB ReFIX · Ctrl+Alt+H";tray.ContextMenuStrip=menu;
                tray.MouseClick+=delegate(object sender,Forms.MouseEventArgs e){if(e.Button==Forms.MouseButtons.Left)toggle();};
                bool render=args.Length>=2 && args[0]=="--render";tray.Visible=!render;
                uint own=(uint)Process.GetCurrentProcess().Id;
                Native.WinEventProc observer=delegate(IntPtr hook,uint evt,IntPtr target,int obj,int child,uint thread,uint time)
                {
                    if(session==null || !session.IsVisible)return;
                    uint current;Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out current);
                    if(current!=0 && current!=own)session.Hide();
                };
                IntPtr foreground=render?IntPtr.Zero:Native.SetWinEventHook(3,3,IntPtr.Zero,observer,0,0,0);
                EventHandler firstShow=null;
                try
                {
                    if(render){if(session==null)session=OpenSession(themes,modules);session.Render(args[1]);return;}
                    if(!args.Contains("--background"))
                    {
                        firstShow=delegate{Forms.Application.Idle-=firstShow;show();};Forms.Application.Idle+=firstShow;
                    }
                    if(!hotkey)tray.ShowBalloonTip(5000,"HUB ReFIX","Ctrl+Alt+H занято другой программой. Открывайте HUB через значок в трее.",Forms.ToolTipIcon.Info);
                    Forms.Application.Run();
                }
                finally
                {
                    if(firstShow!=null)Forms.Application.Idle-=firstShow;
                    tray.Visible=false;if(hotkey)Native.UnregisterHotKey(listener.Handle,1);if(foreground!=IntPtr.Zero)Native.UnhookWinEvent(foreground);
                    if(session!=null)session.Close();GC.KeepAlive(observer);
                }
            }
        }
        private sealed class TrayListener:Forms.NativeWindow,IDisposable
        {
            internal event Action<int> Message;
            internal TrayListener(string name){CreateHandle(new Forms.CreateParams { Caption=name,Style=unchecked((int)0x80000000) });}
            protected override void WndProc(ref Forms.Message message){Action<int> handler=Message;if(handler!=null)handler(message.Msg);base.WndProc(ref message);}
            public void Dispose(){DestroyHandle();}
        }
    }
}
