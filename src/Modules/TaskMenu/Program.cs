using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using ReFix.Shared;

namespace ReFix.TaskMenu
{
    internal static class Program
    {
        [STAThread]static void Main(string[] args)
        {
            bool created;using(var mutex=new Mutex(true,"Local\\"+ModuleRuntime.PipeName("task-menu"),out created)){
                if(!created)return;Paths.Ensure();var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                using(var themes=new ThemeStore(Paths.Root,"task-menu"))using(var controls=new ControlsStore(Paths.Root))using(var source=new HwndSource(new HwndSourceParameters("ReFIX.TaskMenu.Events"){Width=0,Height=0,WindowStyle=unchecked((int)0x80000000)}))using(var topology=new Topology(source))using(var controller=new Controller(app,themes,controls,topology))using(var keys=new GlobalKeyRegistration(controls,"task-menu.toggle",source.Handle,0x5300,controller.Toggle)){
                    source.AddHook(delegate(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){handled=keys.ProcessMessage(msg,w);return IntPtr.Zero;});
                    using(var runtime=new ModuleRuntime("task-menu",delegate{app.Dispatcher.BeginInvoke(new Action(controller.ShowSettings));},delegate{app.Dispatcher.BeginInvoke(new Action(app.Shutdown));})){runtime.Start();if(Array.IndexOf(args,"--background")<0||controller.Empty)app.Dispatcher.BeginInvoke(new Action(controller.ShowSettings));app.Run();}
                }
            }
        }
    }
    internal sealed class Controller : IDisposable
    {
        readonly Application app;readonly ThemeStore themes;readonly ControlsStore controls;readonly Topology topology;readonly TaskMenuStore store;readonly TaskMenuConfig config;readonly List<DockWindow> docks=new List<DockWindow>();readonly System.Windows.Forms.NotifyIcon tray;SettingsWindow settings;bool disposed,rebuildPending;
        internal bool Empty{get{return config.Panels.Count==0;}}
        internal Controller(Application app,ThemeStore themes,ControlsStore controls,Topology topology)
        {
            this.app=app;this.themes=themes;this.controls=controls;this.topology=topology;store=new TaskMenuStore(Paths.Root);config=store.Load();
            var menu=new System.Windows.Forms.ContextMenuStrip();menu.Items.Add("Настроить панели…",null,delegate{app.Dispatcher.BeginInvoke(new Action(ShowSettings));});menu.Items.Add("Раскрыть / свернуть",null,delegate{app.Dispatcher.BeginInvoke(new Action(Toggle));});menu.Items.Add("Выключить меню задач",null,delegate{app.Dispatcher.BeginInvoke(new Action(app.Shutdown));});
            tray=new System.Windows.Forms.NotifyIcon{Text="ReFIX · Меню задач v"+ProductInfo.Version,Icon=System.Drawing.SystemIcons.Application,ContextMenuStrip=menu,Visible=true};tray.DoubleClick+=delegate{app.Dispatcher.BeginInvoke(new Action(ShowSettings));};
            topology.Changed+=OnTopology;themes.Changed+=OnTheme;controls.Changed+=OnControls;Rebuild();
        }
        void OnTopology(object sender,EventArgs e){QueueRebuild();if(settings!=null){settings.Close();settings=null;ShowSettings();}}
        void OnTheme(object sender,EventArgs e){app.Dispatcher.BeginInvoke(new Action(delegate{if(disposed)return;foreach(var dock in docks)dock.ApplyTheme(themes.Current);if(settings!=null)settings.ApplyTheme(themes.Current);}));}
        void OnControls(object sender,EventArgs e){app.Dispatcher.BeginInvoke(new Action(delegate{if(disposed)return;foreach(var dock in docks)dock.BehaviorChanged();}));}
        internal void ShowSettings(){if(disposed)return;if(settings==null){settings=new SettingsWindow(config,topology,themes.Current,Save);settings.Closed+=delegate{settings=null;};}settings.Show();settings.Activate();}
        internal void Toggle(){bool open=false;foreach(var dock in docks)if(dock.IsExpanded)open=true;foreach(var dock in docks)dock.SetExpanded(!open);if(docks.Count==0)ShowSettings();}
        void Save(){try{store.Save(config);QueueRebuild();}catch(IOException ex){MessageBox.Show(ex.Message,"Не удалось сохранить меню задач");}catch(UnauthorizedAccessException ex){MessageBox.Show(ex.Message,"Не удалось сохранить меню задач");}}
        void QueueRebuild(){if(disposed||rebuildPending)return;rebuildPending=true;app.Dispatcher.BeginInvoke(new Action(delegate{rebuildPending=false;if(!disposed)Rebuild();}));}
        void Rebuild(){foreach(var dock in docks)dock.Shutdown();docks.Clear();foreach(var panel in config.Panels){MonitorInfo found=null;foreach(var monitor in topology.Monitors)if(String.Equals(monitor.Id,panel.Monitor,StringComparison.OrdinalIgnoreCase)){found=monitor;break;}if(found==null||found.Forbidden.Contains(panel.Edge))continue;var window=new DockWindow(panel,found,themes.Current,controls,Save,ShowSettings);docks.Add(window);window.Show();}}
        public void Dispose(){if(disposed)return;disposed=true;topology.Changed-=OnTopology;themes.Changed-=OnTheme;controls.Changed-=OnControls;tray.Visible=false;tray.ContextMenuStrip.Dispose();tray.Dispose();if(settings!=null)settings.Close();foreach(var dock in docks)dock.Shutdown();docks.Clear();}
    }
}
