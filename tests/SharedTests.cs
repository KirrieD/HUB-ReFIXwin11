using System;
using System.IO;
using System.Threading;
using System.Reflection;
using System.Web.Script.Serialization;
using System.IO.Pipes;
using System.Text;

public static class SharedTests
{
    static void Assert(bool value, string message) { if (!value) throw new Exception(message); }
    public static void Run(Action<string,bool> report)
    {
        string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "refix-shared-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        string old = Environment.GetEnvironmentVariable("REFIX_DATA_ROOT");
        Environment.SetEnvironmentVariable("REFIX_DATA_ROOT", root);
        try {
            Assembly sdk = Assembly.LoadFrom(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ReFix.Shared.dll"));
            Type profileType = sdk.GetType("ReFix.Shared.ThemeProfile", true);
            dynamic p = Activator.CreateInstance(profileType);
            p.Background = "bad"; p.BackgroundOpacity = -1.0; p.FontSize = double.NaN; p.ImageX = 4.0; p.GlowRadius = 80.0;
            p.Normalize();
            Assert(p.Background == "#202020" && p.BackgroundOpacity == 0.0 && p.FontSize == 13.0 && p.ImageX == 1.0 && p.GlowRadius == 40.0,"Normalization must reject invalid colors and clamp finite ranges");
            dynamic copy = p.Clone(); copy.Accent = "#123456";
            Assert(p.Accent != copy.Accent,"Clone must be independent");
            report("theme normalization and clone",true);
            dynamic customSystem = p.Clone(); customSystem.Mode="System"; customSystem.FontSize=20.0; customSystem.CornerRadius=24.0;
            dynamic resolvedSystem=profileType.GetMethod("Resolve").Invoke(null,new object[]{customSystem});
            Assert(resolvedSystem.FontSize==System.Windows.SystemFonts.MessageFontSize && resolvedSystem.CornerRadius!=24.0,"System mode must use system typography and default geometry rather than custom settings");
            Exception themeFailure=null;
            var uiTest=new Thread(delegate(){try{
                string imageFile=Path.Combine(root,"wide.bmp");
                using(var writer=new BinaryWriter(File.Create(imageFile))){writer.Write((short)0x4d42);writer.Write(78);writer.Write(0);writer.Write(54);writer.Write(40);writer.Write(4);writer.Write(2);writer.Write((short)1);writer.Write((short)24);writer.Write(0);writer.Write(24);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(0);writer.Write(new byte[24]);}
                Assembly wpf=Assembly.Load("PresentationFramework, Version=4.0.0.0, Culture=neutral, PublicKeyToken=31bf3856ad364e35");
                dynamic window=Activator.CreateInstance(wpf.GetType("System.Windows.Window"));window.Width=100.0;window.Height=100.0;
                dynamic visual=Activator.CreateInstance(profileType);visual.Mode="Custom";visual.ImagePath=imageFile;visual.ImageX=.8;visual.ImageY=.2;visual.BackgroundOpacity=.3;
                MethodInfo apply=sdk.GetType("ReFix.Shared.ThemeApplier").GetMethod("Apply");apply.Invoke(null,new object[]{window,visual});
                dynamic brush=window.Resources["Hub.Image"];Assert(Math.Abs((double)brush.Viewbox.X-.4)<.0001 && Math.Abs((double)brush.Viewbox.Width-.5)<.0001,"UniformToFill must continuously crop using target aspect");
                object first=brush.ImageSource;visual.ImageX=.3;apply.Invoke(null,new object[]{window,visual});brush=window.Resources["Hub.Image"];
                Assert(Object.ReferenceEquals(first,(object)brush.ImageSource),"Slider updates must reuse decoded images");Assert(Math.Abs((double)brush.Viewbox.X-.15)<.0001,"Pan must respond continuously");
                Assert((double)window.Opacity==1.0 && Math.Abs((double)window.Resources["Hub.Background"].Opacity-.3)<.0001,"Background alpha must keep text opacity intact");window.Close();
            }catch(Exception e){themeFailure=e;}});uiTest.SetApartmentState(ApartmentState.STA);uiTest.Start();uiTest.Join();if(themeFailure!=null)throw themeFailure;
            report("cached images, continuous pan and background alpha",true);
            Type storeType = sdk.GetType("ReFix.Shared.ThemeStore", true);
            dynamic store = Activator.CreateInstance(storeType, new object[]{root});
            try {
                p.Mode = "Custom"; p.Accent = "#123456"; store.Save(p);
                Assert(!File.Exists(Path.Combine(root,"theme.json")),"Save must debounce disk writes while publishing Current immediately"); Assert(store.Current.Accent == "#123456","Save must update Current immediately"); DateTime savedBy=DateTime.UtcNow.AddSeconds(3); while(!File.Exists(Path.Combine(root,"theme.json"))&&DateTime.UtcNow<savedBy)Thread.Sleep(10); Assert(File.Exists(Path.Combine(root,"theme.json")),"Debounced save must persist theme");
                DateTime dragStart=DateTime.UtcNow; bool updatedDuringDrag=false;
                for(int drag=0;drag<20;drag++){p.Accent=drag%2==0?"#AA0000":"#00AA00";store.Save(p);Thread.Sleep(20);if((DateTime.UtcNow-dragStart).TotalMilliseconds>150){string persisted=ReadTheme(Path.Combine(root,"theme.json"));if(persisted.Contains("#AA0000")||persisted.Contains("#00AA00"))updatedDuringDrag=true;}}
                Assert(updatedDuringDrag,"Continuous saves must publish filesystem updates during drag");
                p.Accent="#123456";store.Save(p);Thread.Sleep(150);
                using(var blocked=new FileStream(Path.Combine(root,"theme.json"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){p.Accent="#FEDCBA";store.Save(p);Thread.Sleep(250);}
                DateTime recoveredBy=DateTime.UtcNow.AddSeconds(2);while(!ReadTheme(Path.Combine(root,"theme.json")).Contains("#FEDCBA")&&DateTime.UtcNow<recoveredBy)Thread.Sleep(20);
                Assert(ReadTheme(Path.Combine(root,"theme.json")).Contains("#FEDCBA"),"Transient replace denial must retry pending last edit without another Save");
                Assert(String.IsNullOrEmpty((string)store.LastError),"Successful retry must clear persistence error");
                using(var blocked=new FileStream(Path.Combine(root,"theme.json"),FileMode.Open,FileAccess.Read,FileShare.ReadWrite)){p.Accent="#102030";store.Save(p);Thread.Sleep(850);Assert(!String.IsNullOrEmpty((string)store.LastError),"Persistent write failure must be observable");}
                Thread.Sleep(200);Assert(!ReadTheme(Path.Combine(root,"theme.json")).Contains("#102030"),"Retries must stop after bounded attempts");
                p.Accent="#123456";store.Save(p);Thread.Sleep(150);
                dynamic reader = Activator.CreateInstance(storeType,new object[]{root});
                try { Assert(reader.Current.Accent == "#123456","Saved theme must reload"); } finally { reader.Dispose(); }
                dynamic external = p.Clone(); external.Accent = "#654321";
                File.WriteAllText(Path.Combine(root,"theme.json"),new JavaScriptSerializer().Serialize(external));
                DateTime until = DateTime.UtcNow.AddSeconds(4);
                while (store.Current.Accent != "#654321" && DateTime.UtcNow < until) Thread.Sleep(20);
                Assert(store.Current.Accent == "#654321","External writes must update theme");
                File.WriteAllText(Path.Combine(root,"bad.png"),"not an image");
                bool rejected=false;
                try { store.ImportBackground(Path.Combine(root,"bad.png")); } catch { rejected=true; }
                Assert(rejected,"Malformed image must be rejected");
                File.WriteAllText(Path.Combine(root,"theme.json"),"{broken"); Thread.Sleep(100); Assert(store.Current.Accent == "#654321","Malformed external theme must retain last valid profile");
                string large=Path.Combine(root,"large.png"); using(var sized=File.Create(large)) sized.SetLength(20*1024*1024+1); rejected=false; try{store.ImportBackground(large);}catch{rejected=true;} Assert(rejected,"Oversized image must be rejected before decode");
                p.Accent="#ABCDEF"; store.Save(p); store.Dispose(); dynamic flushed=Activator.CreateInstance(storeType,new object[]{root}); try{Assert(flushed.Current.Accent=="#ABCDEF","Dispose must flush pending theme save");}finally{flushed.Dispose();}
                report("theme persistence, external changes and invalid image",true);
            } finally { store.Dispose(); }
            Type rt = sdk.GetType("ReFix.Shared.ModuleRuntime",true);
            bool badId=false;
            try { Activator.CreateInstance(rt,new object[]{"../bad",new Action(delegate{}),new Action(delegate{})}); } catch { badId=true; }
            Assert(badId,"Module IDs must be path safe");
            MethodInfo scopedName=rt.GetMethod("PipeName",new Type[]{typeof(string),typeof(string)});
            Assert(scopedName!=null,"Explicit-root PipeName overload must isolate manager sessions");
            string scopedA=(string)scopedName.Invoke(null,new object[]{"sample",root});
            string scopedB=(string)scopedName.Invoke(null,new object[]{"sample",Path.Combine(root,"other")});
            Assert(scopedA!=scopedB,"Distinct data roots must use distinct pipes");
            string id="sharedtest"+Guid.NewGuid().ToString("N");
            int shown=0, stopped=0;
            dynamic runtime=Activator.CreateInstance(rt,new object[]{id,new Action(delegate{Interlocked.Increment(ref shown);}),new Action(delegate{Interlocked.Increment(ref stopped);})});
            try {
                runtime.Start();
                string pipe=(string)rt.GetMethod("PipeName",new Type[]{typeof(string)}).Invoke(null,new object[]{id});
                string reply=Command(pipe,"status");
                var status=new JavaScriptSerializer().DeserializeObject(reply) as System.Collections.Generic.Dictionary<string,object>;
                Assert((string)status["id"]==id && (string)status["state"]=="ON" && Convert.ToInt32(status["pid"])==System.Diagnostics.Process.GetCurrentProcess().Id && Convert.ToInt32(status["protocol"])==1,"Status must describe actual running process");
                Command(pipe,"show"); Command(pipe,"stop");
                SpinWait.SpinUntil(delegate{return shown==1 && stopped==1;},1000); Assert(shown==1 && stopped==1,"Commands must dispatch callbacks");
                Assert(File.Exists(Path.Combine(root,"state",id+".json")),"Live runtime must publish state");
            } finally { runtime.Dispose(); }
            Assert(!File.Exists(Path.Combine(root,"state",id+".json")),"Dispose must remove owned state");
            int configured=0;dynamic configurable=Activator.CreateInstance(rt,new object[]{id,new Action(delegate{}),new Action(delegate{}),new Action(delegate{Interlocked.Increment(ref configured);})});try{configurable.Start();string settingsPipe=(string)rt.GetMethod("PipeName",new Type[]{typeof(string)}).Invoke(null,new object[]{id});string settingsReply=Command(settingsPipe,"settings");SpinWait.SpinUntil(delegate{return configured==1;},1000);Assert(settingsReply.Contains("true")&&configured==1,"Settings command must dispatch only its dedicated callback");}finally{configurable.Dispose();}
            dynamic older=Activator.CreateInstance(rt,new object[]{id,new Action(delegate{}),new Action(delegate{})}); older.Start();
            try {dynamic duplicate=Activator.CreateInstance(rt,new object[]{id,new Action(delegate{}),new Action(delegate{})}); bool refused=false; try{duplicate.Start();}catch{refused=true;}finally{duplicate.Dispose();} Assert(refused,"Duplicate runtimes must not replace live identity"); File.WriteAllText(Path.Combine(root,"state",id+".json"),"{\"instance\":\"replacement\"}");}finally{older.Dispose();} Assert(File.Exists(Path.Combine(root,"state",id+".json")),"Runtime must not delete replacement state");
            report("module pipe commands, process identity and cleanup",true);
        } finally { Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",old); try{Directory.Delete(root,true);}catch{} }
    }
    static string ReadTheme(string path) {
        for(int attempt=0;;attempt++)try{using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new StreamReader(stream,Encoding.UTF8))return reader.ReadToEnd();}catch(IOException){if(attempt>=4)throw;Thread.Sleep(10);}
    }
    static string Command(string name,string command) {
        using(var pipe=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous)) {
            pipe.Connect(2000);
            byte[] bytes=Encoding.UTF8.GetBytes(command+"\n"); pipe.Write(bytes,0,bytes.Length); pipe.Flush();
            using(var reader=new StreamReader(pipe,Encoding.UTF8)) return reader.ReadLine();
        }
    }
#if SHARED_TEST_RUNNER
    public static int Main() {try{Run(delegate(string s,bool b){Console.WriteLine("PASS "+s);});return 0;}catch(Exception e){Console.WriteLine("FAIL "+e);return 1;}}
#endif
}
