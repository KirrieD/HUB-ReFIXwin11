using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using ReFix.Shared;
using ReFix.TaskMenu;
namespace ReFix.MiniIcons
{
    internal static class Program
    {
        [STAThread]static void Main(string[] args)
        {
            bool created;using(var mutex=new Mutex(true,"Local\\"+ModuleRuntime.PipeName("mini-icons"),out created)){
                if(!created)return;Paths.Ensure();var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                using(var themes=new ThemeStore(Paths.Root,"mini-icons"))using(var controls=new ControlsStore(Paths.Root))using(var source=new HwndSource(new HwndSourceParameters("ReFIX.MiniIcons.Events"){Width=0,Height=0,WindowStyle=unchecked((int)0x80000000)}))using(var topology=new Topology(source))using(var controller=new Controller(app,themes,controls,topology))using(var keys=new GlobalKeyRegistration(controls,"mini-icons.toggle",source.Handle,0x5400,controller.Toggle)){
                    source.AddHook(delegate(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){handled=keys.ProcessMessage(msg,w);return IntPtr.Zero;});
                    using(var runtime=new ModuleRuntime("mini-icons",delegate{app.Dispatcher.BeginInvoke(new Action(controller.Toggle));},delegate{app.Dispatcher.BeginInvoke(new Action(app.Shutdown));})){runtime.Start();if(Array.IndexOf(args,"--background")<0)controller.ShowPopup();app.Run();}
                }
            }
        }
    }
}
