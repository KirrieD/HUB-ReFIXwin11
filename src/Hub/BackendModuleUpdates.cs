using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace ReFix.Hub
{
 public sealed partial class ModuleManager
 {
  // One gate ownership covers preparation, shutdown, activation and recovery.
  public Task UpdateAsync(ModuleView view)
  {
   return Work(()=>{
    CheckView(view);view.IsBusy=true;
    string stage=null,download=null,target=null,backup=null,previousDirectory=null,parent=null;
    InstalledRecord previous=null;bool wasOn=false,interrupted=false,promoted=false,backedUp=false,pointerChanged=false;
    string previousPointer=null;Process launched=null;
    try {
     var manifest=view.Manifest;manifest.Validate();
     previous=Installed(view,out previousDirectory);
     if(previous==null)throw new InvalidOperationException("Install the module before updating.");
     string oldRepository=string.IsNullOrEmpty(previous.Manifest.SourceRepository)?"":ReFix.Shared.SourcePolicy.Repository(previous.Manifest.SourceRepository);
     string nextRepository=string.IsNullOrEmpty(manifest.SourceRepository)?"":ReFix.Shared.SourcePolicy.Repository(manifest.SourceRepository);
     if(!string.Equals(oldRepository,nextRepository,StringComparison.OrdinalIgnoreCase)||!string.Equals(previous.Manifest.SourceFolder??"",manifest.SourceFolder??"",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("An update cannot change the installed module source.");
     EnsureInstallSource(manifest);
     if(previous.Manifest.Kind!=manifest.Kind)throw new InvalidDataException("An update cannot change module kind.");
     if(new Version(manifest.Version)<new Version(previous.Manifest.Version))throw new InvalidDataException("An update cannot downgrade the installed module.");
     parent=UninstallDirectory(manifest.Id);VerifyRemovalTree(parent);
     previousPointer=File.ReadAllText(Path.Combine(parent,"current.json"));
     if(string.IsNullOrEmpty(manifest.Sha256))throw new InvalidDataException("No verified package hash is published.");
     string package=FindBundle(manifest);
     if(package==null){download=Path.Combine(root,"download-"+Guid.NewGuid().ToString("N")+".zip");File.WriteAllBytes(download,DownloadModulePackage(manifest));package=download;}
     if(!string.Equals(PackageSafety.Hash(package),manifest.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Package SHA256 mismatch.");
     stage=Path.Combine(parent,".stage-"+Guid.NewGuid().ToString("N"));PackageSafety.Extract(package,stage);
     if(!File.Exists(PackageSafety.Inside(stage,manifest.EntryPoint)))throw new InvalidDataException("Package entry point missing.");
     var record=new InstalledRecord{Manifest=manifest,Files=new Dictionary<string,string>()};
     foreach(string file in Directory.GetFiles(stage,"*",SearchOption.AllDirectories)){
      string relative=file.Substring(stage.Length+1).Replace('\\','/');
      if(string.Equals(relative,"installed.json",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Reserved package metadata.");
      record.Files.Add(relative,PackageSafety.Hash(file));
     }
     Atomic(Path.Combine(stage,"installed.json"),Json().Serialize(record));VerifyRemovalTree(stage);
     // Inspect the installed runtime, never the available manifest, before stopping it.
     Dictionary<string,object> identity=null;
     if(previous.Manifest.Kind=="fix")wasOn=UpdateFixState(previous.Manifest,previousDirectory)=="ON";
     else {identity=Identity(previous.Manifest,previousDirectory);if(identity!=null){VerifyIdentity(Pipe(manifest.Id,"status"),identity,manifest.Id);wasOn=true;}}
     if(wasOn){
      interrupted=true;
      if(previous.Manifest.Kind=="fix")UpdateDisableFix(previous.Manifest,previousDirectory);
      else StopVerifiedProcess(previous.Manifest,identity);
     }
     // Preflight must finish before shutdown; revalidate paths immediately before moves.
     VerifyRemovalTree(parent);
     target=Path.Combine(parent,manifest.Version);backup=target+".backup-"+Guid.NewGuid().ToString("N");
     if(Directory.Exists(target)){Directory.Move(target,backup);backedUp=true;}
     Directory.Move(stage,target);stage=null;promoted=true;
     if(wasOn)launched=UpdateActivate(manifest,target);
     // Publish the new version only after direct candidate health has succeeded.
     Atomic(Path.Combine(parent,"current.json"),Json().Serialize(manifest.Version));pointerChanged=true;
     RefreshState(view);
     if(view.InstalledVersion!=manifest.Version||view.State!=(wasOn?"ON":"OFF"))throw new IOException("Updated module did not verify the expected state.");
     view.Detail=wasOn?"Updated verified package and restored active state.":"Updated verified package. Module remains stopped.";
     // Cleanup is after the commit: a locked backup must not undo a healthy install.
     if(backedUp){
      try{VerifyRemovalTree(backup);Directory.Delete(backup,true);backedUp=false;}
      catch(IOException){view.Detail+=" Previous package backup retained for later cleanup.";}
      catch(UnauthorizedAccessException){view.Detail+=" Previous package backup retained for later cleanup.";}
     }
    } catch(Exception failure) {
     Exception recoveryFailure=null;
     try {
      if(promoted){
       if(wasOn){if(view.Manifest.Kind=="fix")UpdateDisableFix(view.Manifest,target);else UpdateStopCandidate(view.Manifest,target,launched);}
       if(pointerChanged)Atomic(Path.Combine(parent,"current.json"),previousPointer);
       VerifyRemovalTree(target);Directory.Delete(target,true);promoted=false;
      }
      if(backedUp){Directory.Move(backup,target);backedUp=false;}
      if(interrupted&&previous!=null){
       // A rejected stop can leave the old runtime healthy; do not launch a duplicate.
       if(previous.Manifest.Kind=="fix"){if(UpdateFixState(previous.Manifest,previousDirectory)!="ON")UpdateActivate(previous.Manifest,previousDirectory);}
       else if(Identity(previous.Manifest,previousDirectory)==null){using(var restored=UpdateActivate(previous.Manifest,previousDirectory)){}}
      }
     } catch(Exception e){recoveryFailure=e;}
     RefreshState(view);
     view.Detail="Update failed: "+failure.Message+(recoveryFailure==null?" Previous installation retained.":" Recovery requires attention: "+recoveryFailure.Message);
     if(recoveryFailure!=null)throw new AggregateException("Module update and recovery failed.",failure,recoveryFailure);
     throw;
    } finally {
     if(launched!=null)launched.Dispose();
     try{if(stage!=null&&Directory.Exists(stage)){VerifyRemovalTree(stage);Directory.Delete(stage,true);}}finally{
      try{if(download!=null&&File.Exists(download))File.Delete(download);}finally{view.IsBusy=false;}
     }
    }
   });
  }
  string UpdateFixState(ModuleManifest manifest,string directory)
  {
   var status=Fix(manifest,directory,manifest.StatusArgument);VerifyFix(status,manifest.Id);return GetString(status,"state");
  }
  void UpdateDisableFix(ModuleManifest manifest,string directory)
  {
   var result=Fix(manifest,directory,manifest.DisableArgument);VerifyFix(result,manifest.Id);
   if(GetString(result,"state")!="OFF"||UpdateFixState(manifest,directory)!="OFF")throw new IOException("Fix rollback did not verify OFF; installation retained.");
  }
  Process UpdateActivate(ModuleManifest manifest,string directory)
  {
   if(manifest.Kind=="fix"){
    var result=Fix(manifest,directory,manifest.EnableArgument);VerifyFix(result,manifest.Id);
    if(GetString(result,"state")!="ON"||UpdateFixState(manifest,directory)!="ON")throw new IOException("Updated fix did not verify ON.");
    return null;
   }
   var info=new ProcessStartInfo(PackageSafety.Inside(directory,manifest.EntryPoint),"--background"){UseShellExecute=false,WorkingDirectory=directory};info.EnvironmentVariables["REFIX_DATA_ROOT"]=root;
   Process process=Process.Start(info);
   try {
    var timer=Stopwatch.StartNew();Exception last=null;
    while(timer.ElapsedMilliseconds<5000){
     if(process.HasExited)throw new IOException("Updated module exited before control-pipe health verified.");
     try {var identity=Identity(manifest,directory);if(identity!=null){VerifyIdentity(Pipe(manifest.Id,"status"),identity,manifest.Id);return process;}}
     catch(IOException e){last=e;}catch(TimeoutException e){last=e;}
     Thread.Sleep(100);
    }
    throw new IOException("Updated module did not verify control-pipe health.",last);
   } catch {try{UpdateStopCandidate(manifest,directory,process);}finally{process.Dispose();}throw;}
  }
  void UpdateStopCandidate(ModuleManifest manifest,string directory,Process launched)
  {
   try {var identity=Identity(manifest,directory);if(identity!=null)StopVerifiedProcess(manifest,identity);}
   catch {if(launched==null)throw;}
   if(launched==null||launched.HasExited)return;
   // Only this transaction's child handle may be terminated after failed startup.
   // PID discovery is never used for forced termination.
   if(!string.Equals(launched.MainModule.FileName,PackageSafety.Inside(directory,manifest.EntryPoint),StringComparison.OrdinalIgnoreCase))throw new IOException("Cannot verify candidate executable for recovery.");
   launched.Kill();if(!launched.WaitForExit(3000))throw new IOException("Candidate process did not exit during recovery.");
  }
 }
}
