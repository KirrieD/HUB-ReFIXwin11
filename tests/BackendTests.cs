using System;
using System.IO;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using System.IO.Compression;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
using ReFix.Hub;
public static class BackendTests {
 public static void Run(Action<string,bool> report) {
  string root=Path.Combine(Path.GetFullPath("artifacts"),"refix-backend-"+Guid.NewGuid().ToString("N")); Directory.CreateDirectory(root);
  try {
   var manifest=new ModuleManifest {Id="sample",Name="Sample",Description="Test",Version="1.0.0",Kind="process",EntryPoint="sample.exe",Preview="notes"};
   Action<string,Action> test=(name,body)=>{try{body();report(name,true);}catch(Exception e){Console.WriteLine(e);report(name,false);}};
   test("network catalogue accepts UTF8 BOM",()=>{string json="\uFEFF"+new JavaScriptSerializer().Serialize(new CatalogDocument{SchemaVersion=1,Modules=new List<ModuleManifest>{manifest}});var parser=typeof(ModuleManager).GetMethod("ReadCatalog",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);var parsed=(CatalogDocument)parser.Invoke(null,new object[]{json});if(parsed.Modules.Count!=1||parsed.Modules[0].Id!="sample")throw new Exception("Catalogue lost fields");});
   test("reject traversal manifest",()=> { manifest.Id="../escape"; ExpectReject(()=>manifest.Validate());manifest.Id="sample"; });
   test("reject nonrepository downloads",()=> {manifest.PackageUrl="https://example.org/test.zip";ExpectReject(()=>manifest.Validate());manifest.PackageUrl=null;});
   test("process manifests do not require fix arguments",()=>{manifest.EnableArgument="";manifest.DisableArgument="";manifest.StatusArgument="";manifest.Validate();});
   test("reject unsafe entry point",()=> {manifest.EntryPoint="../bad.exe";ExpectReject(()=>manifest.Validate());manifest.EntryPoint="sample.exe";});
   string catalog=Path.Combine(root,"catalog.json"), packages=Path.Combine(root,"packages");Directory.CreateDirectory(packages);
   string archive=Path.Combine(packages,"sample-1.0.0.zip");
   using(var z=ZipFile.Open(archive,ZipArchiveMode.Create)){var e=z.CreateEntry("sample.exe");using(var w=new StreamWriter(e.Open()))w.Write("placeholder");}
   using(var h=SHA256.Create())using(var f=File.OpenRead(archive))manifest.Sha256=BitConverter.ToString(h.ComputeHash(f)).Replace("-","").ToLowerInvariant();
   File.WriteAllText(catalog,new JavaScriptSerializer().Serialize(new CatalogDocument{SchemaVersion=1,Modules=new List<ModuleManifest>{manifest}}));
   using(var manager=new ModuleManager(Path.Combine(root,"data"),catalog)) {
    var view=manager.Modules[0];
    test("initial state NO",()=>{if(view.State!="NO")throw new Exception(view.State);});
    test("verified local package installs OFF",()=>{manager.InstallAsync(view).GetAwaiter().GetResult();if(view.State!="OFF"||view.InstalledVersion!="1.0.0")throw new Exception(view.Detail);});
    test("failed hash leaves installed package intact",()=>{string saved=view.Manifest.Sha256;view.Manifest.Sha256=new string('0',64);ExpectReject(()=>manager.InstallAsync(view).GetAwaiter().GetResult());view.Manifest.Sha256=saved;if(view.State!="OFF"||!view.Detail.Contains("mismatch"))throw new Exception(view.Detail);});
    test("failed backup preserves original package",()=>{string target=Path.Combine(root,"data","modules","sample","1.0.0");using(var locked=OpenDirectory(target,0x80000000,1,IntPtr.Zero,3,0x02000000,IntPtr.Zero)){if(locked.IsInvalid)throw new Exception("Cannot acquire directory rename lock: "+Marshal.GetLastWin32Error());ExpectReject(()=>manager.InstallAsync(view).GetAwaiter().GetResult());if(!File.Exists(Path.Combine(target,"installed.json"))||!File.Exists(Path.Combine(target,"sample.exe")))throw new Exception("Failed backup damaged original package.");}manager.RefreshStatesAsync().GetAwaiter().GetResult();if(view.State!="OFF")throw new Exception(view.Detail);});
    test("module list snapshot survives catalogue replacement",()=>{var snapshot=manager.Modules;int original=snapshot.Count;var replacement=new ModuleManifest{Id="other",Name="Other",Version="1.0.0",EntryPoint="other.exe"};var apply=typeof(ModuleManager).GetMethod("Apply",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);apply.Invoke(manager,new object[]{new CatalogDocument{SchemaVersion=1,Modules=new List<ModuleManifest>{view.Manifest,replacement}}});if(snapshot.Count!=original||manager.Modules.Count!=2)throw new Exception("Observed catalogue snapshot changed.");});
    test("concurrent catalogue readers observe complete snapshots",()=>{var apply=typeof(ModuleManager).GetMethod("Apply",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);var docs=new[]{new CatalogDocument{SchemaVersion=1,Modules=new List<ModuleManifest>{view.Manifest}},new CatalogDocument{SchemaVersion=1,Modules=new List<ModuleManifest>{view.Manifest,new ModuleManifest{Id="other",Name="Other",Version="1.0.0",EntryPoint="other.exe"}}}};var writer=Task.Run(()=>{for(int i=0;i<2000;i++)apply.Invoke(manager,new object[]{docs[i%2]});});var reader=Task.Run(()=>{for(int i=0;i<2000;i++){var snapshot=manager.Modules;int count=snapshot.Count,seen=0;foreach(var item in snapshot){if(item==null)throw new Exception("Incomplete module snapshot.");seen++;}if(count!=seen||snapshot.Count!=count||count<1||count>2)throw new Exception("Catalogue changed during observation.");}});Task.WaitAll(writer,reader);});
    test("tampered installed executable is NO",()=>{File.AppendAllText(Path.Combine(root,"data","modules","sample","1.0.0","sample.exe"),"tamper"); manager.RefreshStatesAsync().GetAwaiter().GetResult();if(view.State!="NO")throw new Exception(view.State);});
   }
   test("ZIP traversal rejected",()=>{string bad=Path.Combine(root,"bad.zip");using(var z=ZipFile.Open(bad,ZipArchiveMode.Create)){z.CreateEntry("../escape");}ExpectReject(()=>PackageSafety.Extract(bad,Path.Combine(root,"stage")));if(File.Exists(Path.Combine(root,"escape")))throw new Exception("escaped");});
  }finally{Directory.Delete(root,true);}
 }
 public static void RunIntegration(Action<string,bool> report) {
  string root=Path.Combine(Path.GetFullPath("artifacts"),"backend-lifecycle-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  try { using(var manager=new ModuleManager(root,Path.GetFullPath("artifacts/catalog.json"))) {
   var view=manager.Modules.First(v=>v.Manifest.Id=="quick-note");
   Action<string,Action> test=(name,body)=>{try{body();report(name,true);}catch(Exception e){Console.WriteLine(e);report(name,false);throw;}};
   try {
    test("integration install OFF",()=>{manager.InstallAsync(view).GetAwaiter().GetResult();if(view.State!="OFF")throw new Exception(view.Detail);});
    test("integration start pipe health ON",()=>{manager.StartAsync(view).GetAwaiter().GetResult();if(view.State!="ON")throw new Exception(view.Detail);});
    test("integration show",()=>manager.ShowAsync(view).GetAwaiter().GetResult());
    test("integration refuses update while running",()=>{ExpectReject(()=>manager.InstallAsync(view).GetAwaiter().GetResult());if(view.State!="ON")throw new Exception(view.Detail);});
    test("integration stop OFF",()=>{manager.StopAsync(view).GetAwaiter().GetResult();if(view.State!="OFF")throw new Exception(view.Detail);});
    test("integration stale process identity OFF",()=>{using(var process=Process.GetCurrentProcess()){File.WriteAllText(Path.Combine(root,"state","quick-note.json"),new JavaScriptSerializer().Serialize(new{id="quick-note",pid=process.Id,startedUtc="1",protocol=1}));}manager.RefreshStatesAsync().GetAwaiter().GetResult();if(view.State!="OFF")throw new Exception(view.Detail);});
   } finally {try{manager.StopAsync(view).GetAwaiter().GetResult();}catch{}}
  }} finally {if(Directory.Exists(root))Directory.Delete(root,true);}
 }
 [DllImport("kernel32.dll",EntryPoint="CreateFileW",CharSet=CharSet.Unicode,SetLastError=true)]
 static extern SafeFileHandle OpenDirectory(string name,uint access,uint sharing,IntPtr security,uint creation,uint flags,IntPtr template);
 static void ExpectReject(Action action){bool rejected=false;try{action();}catch{rejected=true;}if(!rejected)throw new Exception("Expected rejection");}
#if BACKEND_TEST_RUNNER
 public static int Main(string[] args){int failures=0;Action<Action<string,bool>> run=args.Contains("--integration")?(Action<Action<string,bool>>)RunIntegration:Run;try{run((name,ok)=>{Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;});}catch(Exception e){Console.WriteLine(e.Message);return 1;}return failures;}
#endif
}
