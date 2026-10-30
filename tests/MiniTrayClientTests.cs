using System;
using System.IO;
using System.Reflection;
using System.Diagnostics;
using System.Threading;
using System.Windows.Forms;
using System.Web.Script.Serialization;
using ReFix.Shared;
namespace ReFix.Tests
{
    internal static class MiniTrayClientTests
    {
        public static void Run(Action<string,bool> check)
        {
            check("Mini tray has an independent first-party client",typeof(ThemeStore).Assembly.GetType("ReFix.Shared.MiniTrayClient")!=null);
            check("Mini tray supports explicit transfer requests",typeof(MiniTrayFiles).GetMethod("RequestTransfer")!=null);
            check("Tray entry describes scoped icon appearance",typeof(MiniTrayEntry).GetProperty("IconTheme")!=null);
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mini-tray-test-"+Guid.NewGuid().ToString("N"));
            try {
                using(var host=new HostWindow())using(var tray=new NotifyIcon())using(var theme=new ThemeStore(root)) {
                    int left=0,right=0,x=0,y=0;int ui=Thread.CurrentThread.ManagedThreadId,callbackThread=0;
                    using(var client=new MiniTrayClient(root,"hub","Hub","1",tray,delegate{left++;callbackThread=Thread.CurrentThread.ManagedThreadId;},delegate(int px,int py){right++;x=px;y=py;},theme)) {
                        var entries=MiniTrayFiles.ReadEntries(root);check("Live client publishes one verified entry",entries.Count==1);
                        var entry=entries[0];
                        check("Published client carries its current icon theme",entry.IconTheme!=null&&entry.IconTheme.IconFamily==theme.Current.IconFamily);
                        ThemeProfile custom=theme.Current;custom.Mode="Custom";custom.IconFamily="Outline";custom.Accent="#123456";theme.Save(custom);Pump();
                        check("Theme change republishes scoped icon palette",MiniTrayFiles.ReadEntries(root)[0].IconTheme.IconFamily=="Outline"&&MiniTrayFiles.ReadEntries(root)[0].IconTheme.Accent=="#123456");
                        check("Client starts with Windows tray fallback",tray.Visible);
                        var transfer=(ToolStripMenuItem)tray.ContextMenuStrip.Items[tray.ContextMenuStrip.Items.Count-1];
                        check("Transfer menu is disabled without valid host",!transfer.Enabled);
                        MiniTrayFiles.PublishHost(root,host.Handle,new[]{"hub"},host.Handle);Pump();
                        check("Valid selected host hides fallback",!tray.Visible);
                        check("Host publishes optional popup handle",MiniTrayFiles.ReadHost(root).PopupWindowHandle==host.Handle.ToInt64().ToString());
                        check("Selected menu offers return to Windows tray",transfer.Enabled&&transfer.Text=="Вернуть в системный трей");transfer.PerformClick();Pump();
                        check("Return menu routes native request with entry handle",host.Restores==1&&host.EntryHandle==MiniTrayFiles.Handle(entry));
                        check("Transfer request accepts verified host and client",MiniTrayFiles.RequestTransfer(entry,MiniTrayFiles.ReadHost(root),false));Pump();
                        check("Host receives native transfer request",host.Transfers==1&&host.EntryHandle==MiniTrayFiles.Handle(entry));
                        check("Hidden client accepts left activation",MiniTrayFiles.Invoke(entry,false,0,0));
                        check("Hidden client accepts context activation",MiniTrayFiles.Invoke(entry,true,-120000,180000));Pump();
                        check("Actions run on owning UI thread with full signed coordinates",left==1&&right==1&&x==-120000&&y==180000&&callbackThread==ui);
                        MiniTrayFiles.PublishHost(root,host.Handle,new[]{"quiet-mixer"});Pump();check("Unselected host leaves fallback visible",tray.Visible);
                        check("Unselected menu offers transfer",transfer.Text=="Перенести в мини-меню");transfer.PerformClick();Pump();check("Transfer menu routes native request",host.Transfers==2);
                        MiniTrayFiles.PublishHost(root,host.Handle,new[]{"hub"});Pump();
                        MiniTrayFiles.RemoveHost(root);Pump();check("Host removal restores fallback",tray.Visible);
                        var bogus=MiniTrayFiles.CreateEntry("hub","Hub","1",host.Handle);bogus.StartedUtc="1";
                        check("Reused PID with stale start is rejected",!MiniTrayFiles.IsAlive(bogus)&&!MiniTrayFiles.Invoke(bogus,false,0,0));
                        bogus=MiniTrayFiles.CreateEntry("hub","Hub","1",host.Handle);bogus.ExecutablePath+=".impostor";
                        check("Mismatched executable is rejected",!MiniTrayFiles.IsAlive(bogus));
                        MiniTrayFiles.PublishHost(root,host.Handle,new[]{"hub"});
                        string hostPath=MiniTrayFiles.HostPath(root);var stale=MiniTrayFiles.ReadHost(root);stale.StartedUtc="1";File.WriteAllText(hostPath,new JavaScriptSerializer().Serialize(stale));Pump();
                        check("Stale host restores fallback",tray.Visible&&MiniTrayFiles.ReadHost(root)==null);
                        check("Stale host cannot receive transfer request",!MiniTrayFiles.RequestTransfer(entry,stale,false)&&!transfer.Enabled);
                        MiniTrayFiles.RemoveHost(root);check("Host removal preserves another identity",File.Exists(hostPath));
                        File.WriteAllText(hostPath,new string('x',40000));Pump();check("Oversize malformed host restores fallback",tray.Visible&&MiniTrayFiles.ReadHost(root)==null);
                    }
                    check("Dispose removes its entry",MiniTrayFiles.ReadEntries(root).Count==0&&!File.Exists(Path.Combine(root,"tray","entries","hub.json")));
                    check("Dispose removes its owned transfer menu",tray.ContextMenuStrip==null);
                    using(var client=new MiniTrayClient(root,"hub","Hub","1",tray,null,null,theme,false)){check("Explicitly hidden caller keeps fallback hidden",!tray.Visible);}
                    check("Explicit hidden fallback persists on disposal",!tray.Visible);
                    using(var client=new MiniTrayClient(root,"hub","Hub","1",tray,null,null,theme)) {
                        string path=Path.Combine(root,"tray","entries","hub.json");
                        var replacement=MiniTrayFiles.CreateEntry("hub","Replacement","1",host.Handle);MiniTrayFiles.WriteEntry(root,replacement);
                    }
                    check("Dispose preserves another window's replacement entry",MiniTrayFiles.ReadEntries(root).Count==1);
                }
                check("Protocol rejects traversal IDs",!MiniTrayFiles.ValidId("../hub")&&!MiniTrayFiles.ValidId(new string('a',65)));
                var target=new System.Drawing.Rectangle(100,100,80,80);var start=new System.Drawing.Point(90,110);var size=new System.Drawing.Size(8,8);
                check("Drag predicate accepts threshold-crossing drop inside target",MiniTrayClient.DragHit(target,start,new System.Drawing.Point(120,120),size));
                check("Drag predicate rejects release outside target",!MiniTrayClient.DragHit(target,start,new System.Drawing.Point(200,120),size));
                check("Drag predicate rejects click-sized movement",!MiniTrayClient.DragHit(target,new System.Drawing.Point(110,110),new System.Drawing.Point(111,110),size));
                check("Drag predicate handles full signed desktop coordinates",MiniTrayClient.DragHit(new System.Drawing.Rectangle(Int32.MaxValue-5,0,5,10),new System.Drawing.Point(Int32.MinValue,1),new System.Drawing.Point(Int32.MaxValue-3,1),size));
            }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        sealed class HostWindow:NativeWindow,IDisposable
        {
            public int Transfers,Restores;public IntPtr EntryHandle;
            public HostWindow(){CreateHandle(new CreateParams{Parent=new IntPtr(-3)});}
            protected override void WndProc(ref Message message){if(message.Msg==MiniTrayFiles.TransferMessage){Transfers++;EntryHandle=message.WParam;return;}if(message.Msg==MiniTrayFiles.RestoreMessage){Restores++;EntryHandle=message.WParam;return;}base.WndProc(ref message);}
            public void Dispose(){DestroyHandle();}
        }
        static void Pump(){var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<400){Application.DoEvents();Thread.Sleep(10);}}
#if MINI_TRAY_CLIENT_FIXTURE
        [STAThread] public static int Main(string[] args)
        {
            if(args.Length==2&&args[0]=="--host"){using(var window=new HostWindow()){MiniTrayFiles.PublishHost(args[1],window.Handle,new[]{"hub"});Application.Run();}return 0;}
            int failures=0;Action<string,bool> check=delegate(string name,bool ok){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;};
            try{check("Fixture begins without WPF",!WpfLoaded());Run(check);RunCrash(check);check("Tray client and generated icon preserve lazy WPF",!WpfLoaded());}catch(Exception ex){Console.WriteLine("FAIL "+ex);failures++;}return failures==0?0:1;
        }
        static bool WpfLoaded(){foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())if(assembly.GetName().Name=="PresentationFramework")return true;return false;}
        static void RunCrash(Action<string,bool> check)
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mini-tray-crash-"+Guid.NewGuid().ToString("N"));Process child=null;
            try{using(var tray=new NotifyIcon())using(var client=new MiniTrayClient(root,"hub","Hub","1",tray,null,null,null)){
                child=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--host \""+root+"\""){UseShellExecute=false,CreateNoWindow=true});
                var watch=Stopwatch.StartNew();while(tray.Visible&&watch.ElapsedMilliseconds<8000)Pump();
                check("Separate host takes selected fallback",!tray.Visible);
                child.Kill();child.WaitForExit();Pump();check("Process exit restores fallback without file change",tray.Visible);
            }}finally{if(child!=null){if(!child.HasExited)child.Kill();child.Dispose();}if(Directory.Exists(root))Directory.Delete(root,true);}
        }
#endif
    }
}
