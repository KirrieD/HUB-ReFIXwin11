using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using Microsoft.Win32;
using ReFix.Shared;

namespace ReFix.Hub {
 public sealed class LegacyMixerMigrationMarker {public string LegacyPath{get;set;}public string RunValue{get;set;}public bool WasRunning{get;set;}}
 // All access to user registry and legacy processes is behind this injectable boundary.
 public interface ILegacyMixerEnvironment {
  string LocalAppData{get;} string ReadRunValue(); void RemoveRunValue(string expected);bool IsRunning(string path);Task StopAsync(string path);void Restart(string path);
 }
 public interface IMixerMigrationModule {
  void VerifyLocalPackage();bool NeedsInstall{get;}bool IsHealthy{get;}Task StopAsync();Task InstallLocalAsync();Task StartAsync();
 }
 public sealed class LegacyMixerMigration {
  public const string MarkerFileName="migration-quiet-mixer.json";
  readonly string root;readonly ILegacyMixerEnvironment environment;readonly IMixerMigrationModule module;readonly Action saveStartup,restoreStartup;
  public LegacyMixerMigration(ModuleManager manager,string root):this(root,new LegacyEnvironment(),new ManagedMixer(manager),
   new StartupPreference(root)){}
  LegacyMixerMigration(string root,ILegacyMixerEnvironment environment,IMixerMigrationModule module,StartupPreference preference):this(root,environment,module,preference.Save,preference.Restore){}
  sealed class StartupPreference {readonly string root;bool old;public StartupPreference(string root){this.root=root;}public void Save(){using(var store=new ControlsStore(root)){var profile=store.Current;old=profile.StartMixerWithHub;profile.StartMixerWithHub=true;store.Save(profile);}}public void Restore(){using(var store=new ControlsStore(root)){var profile=store.Current;profile.StartMixerWithHub=old;store.Save(profile);}}}
  public LegacyMixerMigration(string root,ILegacyMixerEnvironment environment,IMixerMigrationModule module,Action saveStartup,Action restoreStartup){this.root=Path.GetFullPath(root);this.environment=environment;this.module=module;this.saveStartup=saveStartup;this.restoreStartup=restoreStartup;}
  public async Task RunAsync(){
   string markerPath=Path.Combine(root,MarkerFileName);if(!File.Exists(markerPath))return;
   bool legacyStopped=false,newStarted=false,startupSaved=false;LegacyMixerMigrationMarker marker=null;Exception failure=null;
   try {
    if(new FileInfo(markerPath).Length>8192)throw new InvalidDataException("Migration marker is oversized.");
    marker=new JavaScriptSerializer().Deserialize<LegacyMixerMigrationMarker>(File.ReadAllText(markerPath));
    string known=Path.GetFullPath(Path.Combine(environment.LocalAppData,"QuietMixer","QuietMixer.exe"));
    if(marker==null||string.IsNullOrEmpty(marker.LegacyPath)||!string.Equals(marker.LegacyPath,known,StringComparison.OrdinalIgnoreCase)||!File.Exists(known))throw new InvalidDataException("Migration marker does not identify the known legacy QuietMixer executable.");
    // Reject links in the known install directory; never launch an executable redirected elsewhere.
    for(string p=known;p!=null&&p.Length>=Path.GetFullPath(environment.LocalAppData).Length;p=Path.GetDirectoryName(p))if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Legacy mixer path contains a link.");
    if(!AcceptedRun(marker.RunValue,known)||!string.Equals(environment.ReadRunValue(),marker.RunValue,StringComparison.Ordinal))throw new InvalidOperationException("Legacy QuietMixer startup registration changed; migration was left pending.");
    module.VerifyLocalPackage(); // Mandatory before stopping anything, with no network fallback.
    if(environment.IsRunning(known)){legacyStopped=true;await environment.StopAsync(known).ConfigureAwait(false);}
    if(module.NeedsInstall){if(module.IsHealthy)await module.StopAsync().ConfigureAwait(false);await module.InstallLocalAsync().ConfigureAwait(false);}
    if(!module.IsHealthy){newStarted=true;await module.StartAsync().ConfigureAwait(false);}
    if(!module.IsHealthy)throw new IOException("Adapted mixer did not verify active control-pipe health.");
    saveStartup();startupSaved=true;
    environment.RemoveRunValue(marker.RunValue); // Rechecks exact value immediately before removal.
    // Registration removal is the commit point. Marker cleanup failure must not restart a duplicate mixer.
    try{File.Delete(markerPath);}catch(Exception e){throw new MigrationCommittedException("Mixer migrated successfully, but the pending marker could not be removed: "+e.Message,e);}
   } catch(MigrationCommittedException){throw;}
   catch(Exception error){failure=error;}
   if(failure!=null){
    string rollback="";
    if(newStarted)try{await module.StopAsync().ConfigureAwait(false);}catch(Exception e){rollback+=" New mixer stop failed: "+e.Message;}
    if(startupSaved)try{restoreStartup();}catch(Exception e){rollback+=" Startup preference rollback failed: "+e.Message;}
    if(legacyStopped&&marker!=null)try{environment.Restart(marker.LegacyPath);}catch(Exception e){rollback+=" Legacy mixer restart failed: "+e.Message;}
    throw new InvalidOperationException("QuietMixer migration remains pending. Legacy startup registration was retained. "+failure.Message+rollback,failure);
   }
  }
  static bool AcceptedRun(string value,string path){return value=="\""+path+"\" --background"||value=="\""+path+"\""||value==path;}
  sealed class MigrationCommittedException:IOException {public MigrationCommittedException(string message,Exception inner):base(message,inner){}}
  sealed class ManagedMixer:IMixerMigrationModule {
   readonly ModuleManager manager;ModuleView View{get{var v=manager.Modules.FirstOrDefault(x=>x.Manifest.Id=="quiet-mixer");if(v==null)throw new InvalidOperationException("Bundled catalogue has no quiet-mixer module.");return v;}}
   public ManagedMixer(ModuleManager manager){this.manager=manager;}public void VerifyLocalPackage(){manager.VerifyLocalPackage(View);}public bool NeedsInstall{get{return View.InstalledVersion!=View.Manifest.Version;}}public bool IsHealthy{get{return View.State=="ON";}}
   public Task StopAsync(){return manager.StopAsync(View);}public Task InstallLocalAsync(){return manager.InstallLocalAsync(View);}public Task StartAsync(){return manager.StartAsync(View);}
  }
  sealed class LegacyEnvironment:ILegacyMixerEnvironment {
   const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
   public string LocalAppData{get{return Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);}}
   public string ReadRunValue(){using(var key=Registry.CurrentUser.OpenSubKey(RunKey))return key==null?null:key.GetValue("QuietMixer",null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string;}
   public void RemoveRunValue(string expected){using(var key=Registry.CurrentUser.OpenSubKey(RunKey,true)){if(key==null||key.GetValueKind("QuietMixer")!=RegistryValueKind.String||!string.Equals(key.GetValue("QuietMixer",null,RegistryValueOptions.DoNotExpandEnvironmentNames) as string,expected,StringComparison.Ordinal))throw new IOException("QuietMixer startup value changed during migration.");key.DeleteValue("QuietMixer",true);}}
   static Process[] Matching(string path){var found=new System.Collections.Generic.List<Process>();foreach(var p in Process.GetProcessesByName("QuietMixer")){try{if(!p.HasExited&&string.Equals(p.MainModule.FileName,path,StringComparison.OrdinalIgnoreCase)){found.Add(p);continue;}}catch(Exception e){p.Dispose();foreach(var held in found)held.Dispose();throw new IOException("Cannot safely identify a legacy QuietMixer process.",e);}p.Dispose();}return found.ToArray();}
   public bool IsRunning(string path){var processes=Matching(path);try{return processes.Length!=0;}finally{foreach(var p in processes)p.Dispose();}}
   public Task StopAsync(string path){return Task.Run(()=>{var processes=Matching(path);try{if(processes.Length==0)return;using(var command=Process.Start(new ProcessStartInfo(path,"--exit"){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=Path.GetDirectoryName(path)})){if(command==null||!command.WaitForExit(4000))throw new TimeoutException("Legacy exit command did not finish; no process was killed.");}foreach(var p in processes)if(!p.WaitForExit(4000))throw new TimeoutException("Legacy mixer did not exit gracefully; no process was killed.");if(IsRunning(path))throw new IOException("Legacy mixer remains running.");}finally{foreach(var p in processes)p.Dispose();}});}
   public void Restart(string path){if(IsRunning(path))return;using(var p=Process.Start(new ProcessStartInfo(path,"--background"){UseShellExecute=false,WorkingDirectory=Path.GetDirectoryName(path)})){if(p==null)throw new IOException("Legacy restart did not create a process.");}}
  }
 }
}
