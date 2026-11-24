using System;
using System.Threading;
using System.Windows;
using ReFix.Shared;

namespace ReFix.SystemMonitor
{
    internal static class Program
    {
        [STAThread]static void Main(string[] args)
        {
            bool created;
            using(var mutex=new Mutex(true,"Local\\"+ModuleRuntime.PipeName("system-monitor"),out created))
            {
                if(!created)return;
                Paths.Ensure();
                var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                SynchronizationContext.SetSynchronizationContext(new System.Windows.Threading.DispatcherSynchronizationContext(app.Dispatcher));
                try
                {
                    using(var themes=new ThemeStore(Paths.Root))
                    using(var controller=new Controller(app,themes))
                    using(var tray=new System.Windows.Forms.NotifyIcon{Text="Системный мониторинг",Icon=ReFixIcons.CreateIcon("system-monitor",32,themes.Current),Visible=true})
                    using(var menu=new System.Windows.Forms.ContextMenuStrip())
                    {
                        menu.ShowImageMargin=false;
                        menu.Items.Add("Открыть мониторинг",null,delegate{controller.ShowMain();});
                        menu.Items.Add("Показать / скрыть виджет",null,delegate{controller.Toggle();});
                        menu.Items.Add("Настройки",null,delegate{controller.ShowSettings();});
                        menu.Items.Add("Выключить",null,delegate{app.Shutdown();});
                        tray.ContextMenuStrip=menu;
                        tray.MouseClick+=delegate(object sender,System.Windows.Forms.MouseEventArgs e){if(e.Button==System.Windows.Forms.MouseButtons.Left)controller.Show();};
                        using(var mini=new MiniTrayClient(Paths.Root,"system-monitor","Системный мониторинг",ModuleVersions.SystemMonitor,tray,controller.Show,delegate(int x,int y){menu.Show(x,y);},themes))
                        using(var runtime=new ModuleRuntime("system-monitor",delegate{app.Dispatcher.BeginInvoke(new Action(controller.Show));},delegate{app.Dispatcher.BeginInvoke(new Action(app.Shutdown));},delegate{app.Dispatcher.BeginInvoke(new Action(controller.ShowSettings));}))
                        {
                            runtime.Start();app.Run();
                        }
                        tray.Icon.Dispose();
                    }
                }
                catch(Exception ex){MessageBox.Show(ex.Message,"Системный мониторинг",MessageBoxButton.OK,MessageBoxImage.Warning);}
            }
        }
    }
}
