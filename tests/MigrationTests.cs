using System;
using System.IO;
using System.Collections.Generic;
using System.IO.Compression;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using ReFix.Hub;

public static class MigrationTests {
 sealed class Boundary:ILegacyMixerEnvironment,IMixerMigrationModule {
  public string LocalAppData{get;set;} public string Run; public bool Running=true,Stopped,Restarted,Healthy,FailStart,FailStop,Saved;public List<string> Calls=new List<string>();
  public string ReadRunValue(){return Run;} public void RemoveRunValue(string expected){if(Run!=expected)throw new IOException("Registration changed");Calls.Add("remove-run");Run=null;}
  public bool IsRunning(string path){return Running;} public Task StopAsync(string path){Calls.Add("legacy-stop");if(FailStop)throw new TimeoutException("Legacy still running");Stopped=true;Running=false;return Task.FromResult(0);}
  public void Restart(string path){Restarted=true;Running=true;}public void VerifyLocalPackage(){Calls.Add("verify-local");}public bool NeedsInstall{get{return true;}} public bool IsHealthy{get{return Healthy;}}
  public Task StopAsync(){Calls.Add("module-stop");Healthy=false;return Task.FromResult(0);} public Task InstallLocalAsync(){Calls.Add("install-local");return Task.FromResult(0);}
  public Task StartAsync(){Calls.Add("start");if(FailStart)throw new IOException("New mixer failed health");Healthy=true;return Task.FromResult(0);}
 }
 public static void Run(Action<string,bool> report){
  Action<string,Action> test=(name,body)=>{try{body();report(name,true);}catch(Exception e){Console.WriteLine(e);report(name,false);}};
  test("migration successful health precedes legacy startup removal",()=>Scenario((m,b,root)=>{m.RunAsync().GetAwaiter().GetResult();if(b.Run!=null||!b.Stopped||b.Restarted||!b.Healthy||!b.Saved||File.Exists(Path.Combine(root,LegacyMixerMigration.MarkerFileName)))throw new Exception("Migration did not commit");if(b.Calls.IndexOf("verify-local")>b.Calls.IndexOf("legacy-stop")||b.Calls.IndexOf("start")>b.Calls.IndexOf("remove-run"))throw new Exception("Unsafe migration order");}));
  test("migration failure preserves startup and restarts legacy",()=>Scenario((m,b,root)=>{b.FailStart=true;Reject(()=>m.RunAsync().GetAwaiter().GetResult());if(b.Run==null||!b.Restarted||b.Saved||!File.Exists(Path.Combine(root,LegacyMixerMigration.MarkerFileName)))throw new Exception("Rollback missing");}));
  test("migration rollback follows observed running state rather than installer snapshot",()=>Scenario((m,b,root)=>{string exe=Path.Combine(b.LocalAppData,"QuietMixer","QuietMixer.exe");Write(root,new LegacyMixerMigrationMarker{LegacyPath=exe,RunValue=b.Run,WasRunning=false});b.FailStart=true;Reject(()=>m.RunAsync().GetAwaiter().GetResult());if(!b.Restarted||!b.Running||b.Run==null)throw new Exception("Observed running legacy mixer was not restored");}));
  test("catalogue merge retains bundled upgrade and remote additions",CatalogueMerge);
  test("migration stops old managed mixer before local update",()=>Scenario((m,b,root)=>{b.Healthy=true;m.RunAsync().GetAwaiter().GetResult();if(b.Calls.IndexOf("module-stop")<0||b.Calls.IndexOf("module-stop")>b.Calls.IndexOf("install-local"))throw new Exception("Updated running module");}));
  test("migration never installs if graceful legacy stop fails",()=>Scenario((m,b,root)=>{b.FailStop=true;Reject(()=>m.RunAsync().GetAwaiter().GetResult());if(b.Run==null||b.Calls.Contains("install-local"))throw new Exception("Installed after stop failure");}));
  test("migration rejects changed legacy registration",()=>Scenario((m,b,root)=>{b.Run="other";Reject(()=>m.RunAsync().GetAwaiter().GetResult());if(b.Stopped||b.Run!="other")throw new Exception("Touched changed registration");}));
  test("migration rejects marker outside known legacy path",()=>Scenario((m,b,root)=>{Write(root,new LegacyMixerMigrationMarker{LegacyPath=Path.Combine(root,"other.exe"),RunValue=b.Run,WasRunning=true});Reject(()=>m.RunAsync().GetAwaiter().GetResult());if(b.Stopped)throw new Exception("Touched unrelated process");}));
  test("real manager local migration install verifies hash and refuses missing bundle",()=>{
   string root=Path.Combine(Path.GetFullPath("artifacts"),"migration-local-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(Path.Combine(root,"packages"));
   try{string zip=Path.Combine(root,"packages","quiet-mixer-1.1.0.zip");using(var archive=ZipFile.Open(zip,ZipArchiveMode.Create))using(var writer=new StreamWriter(archive.CreateEntry("mixer.exe").Open()))writer.Write("test package, never launched");
    var manifest=new ModuleManifest{Id="quiet-mixer",Name="Mixer",Version="1.1.0",EntryPoint="mixer.exe",Sha256=PackageSafety.Hash(zip),PackageUrl="https://github.com/KirrieD/HUB-ReFIXwin11/releases/download/v1.1.0/quiet-mixer-1.1.0.zip"};string catalog=Path.Combine(root,"catalog.json");File.WriteAllText(catalog,new JavaScriptSerializer().Serialize(new CatalogDocument{SchemaVersion=1,Modules=new List<ModuleManifest>{manifest}}));
    using(var manager=new ModuleManager(Path.Combine(root,"data"),catalog)){var view=manager.Modules[0];manager.VerifyLocalPackage(view);manager.InstallLocalAsync(view).GetAwaiter().GetResult();if(view.State!="OFF")throw new Exception(view.Detail);File.AppendAllText(zip,"tamper");Reject(()=>manager.VerifyLocalPackage(view));Reject(()=>manager.InstallLocalAsync(view).GetAwaiter().GetResult());File.Delete(zip);Reject(()=>manager.VerifyLocalPackage(view));Reject(()=>manager.InstallLocalAsync(view).GetAwaiter().GetResult());if(view.InstalledVersion!="1.1.0")throw new Exception("Local failure damaged installed module");}
   }finally{Directory.Delete(root,true);}
  });
 }
 static void CatalogueMerge(){
  string root=Path.Combine(Path.GetFullPath("artifacts"),"migration-catalogue-"+Guid.NewGuid().ToString("N"));string data=Path.Combine(root,"data");Directory.CreateDirectory(data);Directory.CreateDirectory(Path.Combine(root,"packages"));
  try{string archive=Path.Combine(root,"packages","quiet-mixer-1.1.0.zip");using(var zip=ZipFile.Open(archive,ZipArchiveMode.Create))using(var writer=new StreamWriter(zip.CreateEntry("mixer.exe").Open()))writer.Write("bundled 1.1 test, never launched");
   var bundled=new ModuleManifest{Id="quiet-mixer",Name="Bundled mixer",Version="1.1.0",EntryPoint="mixer.exe",Sha256=PackageSafety.Hash(archive)};
   var old=new ModuleManifest{Id="quiet-mixer",Name="Old mixer",Version="1.0.0",EntryPoint="mixer.exe",Sha256=new string('0',64)};
   var extra=new ModuleManifest{Id="remote-only",Name="Remote module",Version="1.0.0",EntryPoint="extra.exe"};string catalog=Path.Combine(root,"catalog.json");var json=new JavaScriptSerializer();
   File.WriteAllText(catalog,json.Serialize(new CatalogDocument{SchemaVersion=1,Modules=new List<ModuleManifest>{bundled}}));File.WriteAllText(Path.Combine(data,"catalog-cache.json"),json.Serialize(new CatalogDocument{SchemaVersion=1,Modules=new List<ModuleManifest>{old,extra}}));
   using(var manager=new ModuleManager(data,catalog)){
    var mixer=System.Linq.Enumerable.First(manager.Modules,v=>v.Manifest.Id=="quiet-mixer");if(mixer.Manifest.Version!="1.1.0"||System.Linq.Enumerable.Count(manager.Modules,v=>v.Manifest.Id!="task-menu")!=2)throw new Exception("Old cache replaced bundled upgrade or lost remote module");manager.VerifyLocalPackage(mixer);
    var merge=typeof(ModuleManager).GetMethod("MergeCatalog",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
    var merged=(CatalogDocument)merge.Invoke(manager,new object[]{new CatalogDocument{SchemaVersion=1,Modules=new List<ModuleManifest>{old}}});
    if(System.Linq.Enumerable.First(merged.Modules,v=>v.Id=="quiet-mixer").Version!="1.1.0"||System.Linq.Enumerable.Count(merged.Modules,v=>v.Id!="task-menu")!=2)throw new Exception("Older remote refresh downgraded bundle");
    var same=new ModuleManifest{Id="quiet-mixer",Name="Conflicting same version",Version="1.1.0",EntryPoint="mixer.exe",Sha256=new string('0',64)};
    merged=(CatalogDocument)merge.Invoke(manager,new object[]{new CatalogDocument{SchemaVersion=1,Modules=new List<ModuleManifest>{same}}});if(System.Linq.Enumerable.First(merged.Modules,v=>v.Id=="quiet-mixer").Sha256!=bundled.Sha256)throw new Exception("Same-version remote metadata displaced verified local bundle");
   }
  }finally{Directory.Delete(root,true);}
 }
 static void Scenario(Action<LegacyMixerMigration,Boundary,string> body){string root=Path.Combine(Path.GetFullPath("artifacts"),"migration-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);try{var b=new Boundary{LocalAppData=Path.Combine(root,"local")};string exe=Path.Combine(b.LocalAppData,"QuietMixer","QuietMixer.exe");Directory.CreateDirectory(Path.GetDirectoryName(exe));File.WriteAllText(exe,"test boundary only");b.Run="\""+exe+"\" --background";Write(root,new LegacyMixerMigrationMarker{LegacyPath=exe,RunValue=b.Run,WasRunning=true});var migration=new LegacyMixerMigration(root,b,b,()=>{b.Saved=true;},()=>{b.Saved=false;});body(migration,b,root);}finally{Directory.Delete(root,true);}}
 static void Write(string root,LegacyMixerMigrationMarker marker){File.WriteAllText(Path.Combine(root,LegacyMixerMigration.MarkerFileName),new JavaScriptSerializer().Serialize(marker));}
 static void Reject(Action action){try{action();}catch{return;}throw new Exception("Expected migration rejection");}
}
