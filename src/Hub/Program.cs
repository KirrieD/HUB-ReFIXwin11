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
using System.Threading.Tasks;
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
            set { using(RegistryKey key=Registry.CurrentUser.CreateSubKey(RunKey)) { if(value)key.SetValue("ReFIXHub","\""+Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"HUB-ReFIX.exe")+"\" --background");else key.DeleteValue("ReFIXHub",false); } }
        }
    }
    internal interface IHubSession
    {
        bool IsVisible {get;}
        void Show();void Toggle();void Hide();void OpenStyles();void OpenControls();void SetNotice(string message);void Render(string path);void Close();
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
        private static IHubSession OpenSession(ThemeStore themes,ModuleManager modules,ControlsStore controls,Action updates){return new HubSession(themes,modules,controls,updates);}
        private static void Run(string[] args)
        {
            Paths.Ensure();
            using(ThemeStore themes=new ThemeStore(Paths.Root))
            using(ControlsStore controls=new ControlsStore(Paths.Root))
            using(ModuleManager modules=new ModuleManager(Paths.Root,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog.json")))
            using(TrayListener listener=new TrayListener(Instance))
            using(Forms.NotifyIcon tray=new Forms.NotifyIcon())
            using(Forms.ContextMenuStrip menu=new Forms.ContextMenuStrip())
            {
                IHubSession session=null;
                GlobalKeyRegistration hubKey=null;
                UpdateCoordinator updater=null;
                string notice=args.Contains("--setup-verified")?"Все установлено":args.Contains("--portable-verified")?"Готово к работе · ZIP":"Требуется настройка для корректной работы";
                string backgroundNotice=null;
                Action<string> report=delegate(string message){notice=message;if(session!=null)session.SetNotice(message);};
                Action checkUpdates=delegate{if(updater!=null)updater.Check(true);};
                Action show=delegate
                {
                    try{if(session==null){session=OpenSession(themes,modules,controls,checkUpdates);session.SetNotice(notice);}session.Show();if(updater!=null)updater.Check(false);}
                    catch(Exception ex){Log(ex);Forms.MessageBox.Show("Не удалось открыть интерфейс HUB.\n\n"+ex.Message+"\n\nЗапустите HUB повторно.","HUB ReFIX",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Error);Forms.Application.ExitThread();}
                };
                Action toggle=delegate{if(session==null)show();else session.Toggle();};
                uint recreated=Native.RegisterWindowMessage("TaskbarCreated");
                Action refreshTray=delegate{HotkeyRuntimeStatus state=controls.GetRuntime("hub.toggle");string key=state==null?controls.Current.Get("hub.toggle"):state.Actual;tray.Text="HUB ReFIX · "+(string.IsNullOrEmpty(key)?"трей":key)+(state!=null&&!string.IsNullOrEmpty(state.Error)?" · конфликт":"");};
                listener.Message+=delegate(int message,IntPtr wParam)
                {
                    if(hubKey!=null&&hubKey.ProcessMessage(message,wParam))return;
                    if(message==Toggle)toggle();
                    else if(message==Quit)Forms.Application.ExitThread();
                    else if(message==0x8103){refreshTray();string failure=Interlocked.Exchange(ref backgroundNotice,null);if(failure!=null)report(failure);}
                    else if(message==UpdateCoordinator.Message&&updater!=null)updater.ProcessMessage();
                    else if((uint)message==recreated){tray.Visible=false;tray.Visible=true;}
                };
                hubKey=new GlobalKeyRegistration(controls,"hub.toggle",listener.Handle,100,toggle);
                menu.Items.Add("Открыть HUB",null,delegate{show();});
                menu.Items.Add("Оформление",null,delegate{show();session.OpenStyles();});
                menu.Items.Add("Управление → Назначение клавиш",null,delegate{show();session.OpenControls();});
                menu.Items.Add("Проверить обновления",null,delegate{show();checkUpdates();});
                menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Выйти",null,delegate{Forms.Application.ExitThread();});
                tray.Icon=System.Drawing.SystemIcons.Shield;refreshTray();tray.ContextMenuStrip=menu;
                EventHandler bindingsChanged=delegate{Native.PostMessage(listener.Handle,0x8103,IntPtr.Zero,IntPtr.Zero);};controls.Changed+=bindingsChanged;controls.RuntimeChanged+=bindingsChanged;
                tray.MouseClick+=delegate(object sender,Forms.MouseEventArgs e){if(e.Button==Forms.MouseButtons.Left)toggle();};
                bool render=args.Length>=2 && args[0]=="--render";tray.Visible=!render;
                updater=new UpdateCoordinator(Paths.Root,AppDomain.CurrentDomain.BaseDirectory,listener.Handle,report,delegate{Forms.Application.ExitThread();});
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
                    if(render){if(session==null)session=OpenSession(themes,modules,controls,checkUpdates);session.SetNotice(notice);session.Render(args[1]);return;}
                    if(!args.Contains("--background"))
                    {
                        firstShow=delegate{Forms.Application.Idle-=firstShow;show();};Forms.Application.Idle+=firstShow;
                    }
                    Task startup=Task.Run(delegate{return ResumeMixer(modules,controls);});startup.ContinueWith(delegate(Task failed){Log(failed.Exception);Interlocked.Exchange(ref backgroundNotice,"Не удалось запустить микшер: "+failed.Exception.GetBaseException().Message);Native.PostMessage(listener.Handle,0x8103,IntPtr.Zero,IntPtr.Zero);},TaskContinuationOptions.OnlyOnFaulted);
                    updater.Check(false);
                    Forms.Application.Run();
                }
                finally
                {
                    if(firstShow!=null)Forms.Application.Idle-=firstShow;
                    updater.Dispose();controls.Changed-=bindingsChanged;controls.RuntimeChanged-=bindingsChanged;hubKey.Dispose();tray.Visible=false;if(foreground!=IntPtr.Zero)Native.UnhookWinEvent(foreground);
                    if(session!=null)session.Close();GC.KeepAlive(observer);
                }
            }
        }
        private static async Task ResumeMixer(ModuleManager manager,ControlsStore controls)
        {
            await new LegacyMixerMigration(manager,Paths.Root).RunAsync();
            if(!controls.Current.StartMixerWithHub)return;
            await manager.RefreshStatesAsync();ModuleView mixer=manager.Modules.FirstOrDefault(delegate(ModuleView view){return view.Manifest.Id=="quiet-mixer";});
            if(mixer!=null&&mixer.State=="OFF")await manager.StartAsync(mixer);
        }
        private sealed class TrayListener:Forms.NativeWindow,IDisposable
        {
            internal event Action<int,IntPtr> Message;
            internal TrayListener(string name){CreateHandle(new Forms.CreateParams { Caption=name,Style=unchecked((int)0x80000000) });}
            protected override void WndProc(ref Forms.Message message){Action<int,IntPtr> handler=Message;if(handler!=null)handler(message.Msg,message.WParam);base.WndProc(ref message);}
            public void Dispose(){DestroyHandle();}
        }
    }
}
