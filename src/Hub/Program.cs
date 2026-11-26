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
        internal static string Launcher=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"HUB-ReFIX.exe");
        internal static bool Enabled
        {
            get { try{return !String.IsNullOrEmpty(new RegistryStartupStore().Command);}catch(System.Security.SecurityException){return false;}catch(UnauthorizedAccessException){return false;} }
            set { StartupRegistration.SetEnabled(new RegistryStartupStore(),Launcher,value); }
        }
    }
    internal interface IHubSession
    {
        bool IsVisible {get;}
        void Show();void Toggle();void Hide();void OpenStyles();void OpenControls();void OpenSettings();void SetNotice(string message);void Render(string path);void Close();
    }
    internal static class Program
    {
        internal const int Toggle=0x8101,Quit=0x8102,NewNote=0x8105,NoteMenuChanged=0x8106;
        private static string Instance
        {
            get { using(SHA256 hash=SHA256.Create())return "ReFIXHub."+BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(WindowsIdentity.GetCurrent().User.Value+Paths.Root))).Replace("-","").Substring(0,20); }
        }
        internal static void Log(Exception ex){try{Paths.Ensure();File.WriteAllText(Path.Combine(Paths.Root,"hub-error.log"),ex.ToString());}catch{}}
        internal static bool ShouldAutoHide(bool enabled,uint foregroundProcess,uint ownProcess){return enabled&&foregroundProcess!=0&&foregroundProcess!=ownProcess;}
        [STAThread]
        private static void Main(string[] args)
        {
            try
            {
                if(args.Contains("--exit")){Native.PostMessage(Native.FindWindow(null,Instance),Quit,IntPtr.Zero,IntPtr.Zero);return;}
                int launcherIndex=Array.LastIndexOf(args,"--launcher-path");
                if(launcherIndex>=0 && launcherIndex+1<args.Length)
                {
                    string candidate=Path.GetFullPath(args[launcherIndex+1]);
                    if(File.Exists(candidate)&&String.Equals(Path.GetDirectoryName(candidate).TrimEnd(Path.DirectorySeparatorChar),AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar),StringComparison.OrdinalIgnoreCase))Startup.Launcher=candidate;
                }
                if(args.Contains("--remove-note-shell-menu"))
                {
                    NoteShellMenu.Sync(Startup.Launcher,false);
                    return;
                }
                string startupWarning=null;
                if(!args.Contains("--render") && String.IsNullOrEmpty(Environment.GetEnvironmentVariable("REFIX_DATA_ROOT")))
                {
                    try { if(!File.Exists(Startup.Launcher))Startup.Launcher=Assembly.GetExecutingAssembly().Location;StartupRegistration.Ensure(new RegistryStartupStore(),Startup.Launcher); }
                    catch(Exception ex){Log(ex);startupWarning="Не удалось настроить автозапуск: "+ex.Message;}
                }
                bool created;
                using(Mutex mutex=new Mutex(true,"Local\\"+Instance,out created))
                {
                    if(!created)
                    {
                        if(args.Contains("--new-note"))Native.PostMessage(Native.FindWindow(null,Instance),NewNote,IntPtr.Zero,IntPtr.Zero);
                        else if(!args.Contains("--background"))Native.PostMessage(Native.FindWindow(null,Instance),Toggle,IntPtr.Zero,IntPtr.Zero);
                        return;
                    }
                    Run(args,startupWarning);
                }
            }
            catch(Exception ex){Log(ex);if(!args.Contains("--background"))Forms.MessageBox.Show("Не удалось открыть HUB ReFIX.\n\n"+ex.Message,"HUB ReFIX",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Error);}
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IHubSession OpenSession(ThemeStore themes,ModuleManager modules,ControlsStore controls,Action updates){return new HubSession(themes,modules,controls,updates);}
        private static void Run(string[] args,string startupWarning)
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
                if(startupWarning!=null)notice+=" · "+startupWarning;
                string backgroundNotice=null;
                Action<string> report=delegate(string message){notice=message;if(session!=null)session.SetNotice(message);};
                Action checkUpdates=delegate{if(updater!=null)updater.Check(true);};
                Action show=delegate
                {
                    try{if(session==null){session=OpenSession(themes,modules,controls,checkUpdates);session.SetNotice(notice);}session.Show();if(updater!=null)updater.Check(false);}
                    catch(Exception ex){Log(ex);Forms.MessageBox.Show("Не удалось открыть интерфейс HUB.\n\n"+ex.Message+"\n\nЗапустите HUB повторно.","HUB ReFIX",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Error);Forms.Application.ExitThread();}
                };
                Action toggle=delegate{if(session==null)show();else session.Toggle();};
                Action newNote=async delegate
                {
                    try
                    {
                        var note=modules.Modules.FirstOrDefault(m=>m.Manifest.Id=="quick-note");
                        if(note==null||String.IsNullOrEmpty(note.InstalledVersion))throw new InvalidOperationException("Установите расширение «Быстрая заметка» в HUB.");
                        if(new Version(note.InstalledVersion)<new Version("1.6.3"))throw new InvalidOperationException("Обновите расширение «Быстрая заметка» до версии 1.6.3 или новее.");
                        await modules.RefreshStatesAsync();
                        if(note.State!="ON")await modules.StartAsync(note);
                        await modules.NewNoteAsync(note);
                    }
                    catch(Exception ex){Log(ex);Forms.MessageBox.Show(ex.Message,"Новая заметка",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Warning);}
                };
                uint recreated=Native.RegisterWindowMessage("TaskbarCreated");
                Action refreshTray=delegate{HotkeyRuntimeStatus state=controls.GetRuntime("hub.toggle");string key=state==null?controls.Current.Get("hub.toggle"):state.Actual;tray.Text="HUB ReFIX · "+(string.IsNullOrEmpty(key)?"трей":key)+(state!=null&&!string.IsNullOrEmpty(state.Error)?" · конфликт":"");};
                int menuState=-1;
                Action syncNoteMenu=delegate
                {
                    try { NoteShellMenu.Sync(Startup.Launcher,Volatile.Read(ref menuState)==1); }
                    catch(Exception ex) { Log(ex);report("Не удалось настроить меню «Создать»: "+ex.Message); }
                };
                EventHandler noteInstallationChanged=delegate
                {
                    int state=NoteShellMenu.Supported(modules.Modules.FirstOrDefault(m=>m.Manifest.Id=="quick-note"))?1:0;
                    if(Interlocked.Exchange(ref menuState,state)!=state)
                        Native.PostMessage(listener.Handle,NoteMenuChanged,IntPtr.Zero,IntPtr.Zero);
                };
                listener.Message+=delegate(int message,IntPtr wParam)
                {
                    if(hubKey!=null&&hubKey.ProcessMessage(message,wParam))return;
                    if(message==Toggle)toggle();
                    else if(message==NewNote)newNote();
                    else if(message==NoteMenuChanged)syncNoteMenu();
                    else if(message==Quit)Forms.Application.ExitThread();
                    else if(message==0x8103){refreshTray();string failure=Interlocked.Exchange(ref backgroundNotice,null);if(failure!=null)report(failure);}
                    else if(message==UpdateCoordinator.Message&&updater!=null)updater.ProcessMessage();
                    else if((uint)message==recreated){refreshTray();}
                };
                hubKey=new GlobalKeyRegistration(controls,"hub.toggle",listener.Handle,100,toggle);
                menu.Items.Add("Открыть HUB",null,delegate{show();});
                menu.Items.Add("Настройки",null,delegate{show();session.OpenSettings();});
                menu.Items.Add("Проверить обновления",null,delegate{show();checkUpdates();});
                menu.Items.Add(new Forms.ToolStripSeparator());menu.Items.Add("Выйти",null,delegate{Forms.Application.ExitThread();});
                tray.Icon=System.Drawing.SystemIcons.Shield;refreshTray();tray.ContextMenuStrip=menu;
                EventHandler bindingsChanged=delegate{Native.PostMessage(listener.Handle,0x8103,IntPtr.Zero,IntPtr.Zero);};controls.Changed+=bindingsChanged;controls.RuntimeChanged+=bindingsChanged;
                tray.MouseClick+=delegate(object sender,Forms.MouseEventArgs e){if(e.Button==Forms.MouseButtons.Left)toggle();};
                MiniTrayClient miniTray=null;
                bool render=args.Length>=2 && args[0]=="--render";tray.Visible=!render;
                if(!render)miniTray=new MiniTrayClient(Paths.Root,"hub","HUB ReFIX",ProductInfo.Version,tray,toggle,delegate(int x,int y){menu.Show(x,y);},themes);
                updater=new UpdateCoordinator(Paths.Root,AppDomain.CurrentDomain.BaseDirectory,listener.Handle,report,delegate{Forms.Application.ExitThread();},themes,delegate{return session!=null&&session.IsVisible;});
                uint own=(uint)Process.GetCurrentProcess().Id;
                Native.WinEventProc observer=delegate(IntPtr hook,uint evt,IntPtr target,int obj,int child,uint thread,uint time)
                {
                    if(session==null || !session.IsVisible)return;
                    uint current;Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out current);
                    if(!CaptureVisibility.IsCaptureActive&&ShouldAutoHide(controls.Current.AutoHideHub,current,own))session.Hide();
                };
                IntPtr foreground=render?IntPtr.Zero:Native.SetWinEventHook(3,3,IntPtr.Zero,observer,0,0,0);
                EventHandler firstShow=null;
                try
                {
                    if(render){if(session==null)session=OpenSession(themes,modules,controls,checkUpdates);session.SetNotice(notice);session.Render(args[1]);return;}
                    if(String.IsNullOrEmpty(Environment.GetEnvironmentVariable("REFIX_DATA_ROOT")))
                    {
                        modules.Changed+=noteInstallationChanged;
                        noteInstallationChanged(modules,EventArgs.Empty);
                    }
                    if(args.Contains("--new-note")||!args.Contains("--background"))
                    {
                        firstShow=delegate{Forms.Application.Idle-=firstShow;if(args.Contains("--new-note"))newNote();else show();};Forms.Application.Idle+=firstShow;
                    }
                    Task startup=Task.Run(delegate{return ResumeModules(modules,controls);});startup.ContinueWith(delegate(Task failed){Log(failed.Exception);Interlocked.Exchange(ref backgroundNotice,"Не удалось запустить расширения: "+failed.Exception.GetBaseException().Message);Native.PostMessage(listener.Handle,0x8103,IntPtr.Zero,IntPtr.Zero);},TaskContinuationOptions.OnlyOnFaulted);
                    updater.Check(false);
                    Forms.Application.Run();
                }
                finally
                {
                    modules.Changed-=noteInstallationChanged;
                    if(firstShow!=null)Forms.Application.Idle-=firstShow;
                    if(miniTray!=null)miniTray.Dispose();updater.Dispose();controls.Changed-=bindingsChanged;controls.RuntimeChanged-=bindingsChanged;hubKey.Dispose();tray.Visible=false;if(foreground!=IntPtr.Zero)Native.UnhookWinEvent(foreground);
                    if(session!=null)session.Close();GC.KeepAlive(observer);
                }
            }
        }
        // Retained for existing integrations that call the original startup entry point.
        private static Task ResumeMixer(ModuleManager manager,ControlsStore controls){return ResumeModules(manager,controls);}
        private static async Task ResumeModules(ModuleManager manager,ControlsStore controls)
        {
            var errors=new System.Collections.Generic.List<Exception>();
            try{await new LegacyMixerMigration(manager,Paths.Root).RunAsync();}catch(Exception ex){errors.Add(ex);}
            var profile=controls.Current;
            if(!profile.ModuleStartupMigrated)
            {
                profile=controls.Current;
                foreach(var module in manager.Modules.Where(m=>m.State=="ON"))profile.AdoptRunningModule(module.Manifest.Id);
                profile.ModuleStartupMigrated=true;
                controls.Save(profile);
            }
            if(profile.HasModuleStartup)
            {
                await manager.RefreshStatesAsync();
                try{await ResumeEnabledModules(profile,manager.Modules,manager.StartAtHubStartupAsync);}catch(AggregateException ex){errors.AddRange(ex.InnerExceptions);}
            }
            if(errors.Count!=0)throw new AggregateException(errors);
        }
        internal static async Task ResumeEnabledModules(ControlsProfile profile,System.Collections.Generic.IEnumerable<ModuleView> modules,Func<ModuleView,Task> start)
        {
            var errors=new System.Collections.Generic.List<Exception>();
            foreach(var module in modules.OrderByDescending(view=>view.Manifest.Id=="mini-icons"))
            {
                if(module.State!="OFF"||!profile.StartsWithHub(module.Manifest.Id))continue;
                try{await start(module);}catch(Exception ex){errors.Add(ex);}
            }
            if(errors.Count!=0)throw new AggregateException(errors);
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
