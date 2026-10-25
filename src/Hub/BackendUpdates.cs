using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using ReFix.Shared;
using ReFix.Updating;
namespace ReFix.Hub {
 public sealed class HubRelease {
  public int SchemaVersion{get;set;} public string Version{get;set;} public string PackageUrl{get;set;} public string Sha256{get;set;} public string SetupUrl{get;set;} public string SetupSha256{get;set;} public string Notes{get;set;}
  public void Validate(){if(SchemaVersion!=1)throw new InvalidDataException("Unsupported update schema.");UpdateEngine.Version(Version);HubUpdateService.AssetUrl(PackageUrl,".zip");HubUpdateService.AssetUrl(SetupUrl,".exe");if(!UpdateEngine.IsHash(Sha256)||!UpdateEngine.IsHash(SetupSha256)||(Notes??"").Length>16000)throw new InvalidDataException("Invalid update metadata.");}
 }
 public sealed class PreparedHubUpdate { public string PlanPath{get;internal set;} }
 public sealed class HubUpdateService {
  const string Latest="https://api.github.com/repos/KirrieD/HUB-ReFIXwin11/releases/latest";
  const string LatestMetadata="https://github.com/KirrieD/HUB-ReFIXwin11/releases/latest/download/update.json";
  readonly Func<Uri,HttpWebRequest> createRequest;
  readonly string dataRoot,appDirectory;public event Action<int,string> Progress;
  internal static string DecodeJson(byte[] bytes){string text=new UTF8Encoding(false,true).GetString(bytes);return text.Length>0&&text[0]=='\uFEFF'?text.Substring(1):text;}
  public HubUpdateService(string dataRoot,string appDirectory):this(dataRoot,appDirectory,uri=>(HttpWebRequest)WebRequest.Create(uri)){}
  internal HubUpdateService(string dataRoot,string appDirectory,Func<Uri,HttpWebRequest> createRequest){this.dataRoot=UpdateEngine.Full(dataRoot);this.appDirectory=UpdateEngine.Full(appDirectory);this.createRequest=createRequest;}
  void Notify(int percent,string message){var h=Progress;if(h!=null)h(percent,message);}
  public Task<HubRelease> CheckAsync(){return Task.Run(()=>{
   Notify(0,"Проверка обновлений");Dictionary<string,object> doc;try{doc=UpdateEngine.Json().Deserialize<Dictionary<string,object>>(DecodeJson(Download(Latest,1024*1024,false)));}catch(WebException e){var response=e.Response as HttpWebResponse;if(response!=null&&response.StatusCode==HttpStatusCode.NotFound){response.Dispose();return null;}if(response!=null&&(response.StatusCode==HttpStatusCode.Forbidden||(int)response.StatusCode==429)){response.Dispose();Notify(0,"GitHub API временно недоступен. Проверка официального релиза…");return ReadRelease(LatestMetadata);}throw;}
   object raw;if(doc==null||!doc.TryGetValue("assets",out raw))throw new InvalidDataException("GitHub release has no assets.");var assets=raw as System.Collections.IEnumerable;string metadata=null;if(assets!=null)foreach(object item in assets){var asset=item as Dictionary<string,object>;object name,url;if(asset!=null&&asset.TryGetValue("name",out name)&&Convert.ToString(name)=="update.json"&&asset.TryGetValue("browser_download_url",out url)){if(metadata!=null)throw new InvalidDataException("Duplicate update metadata asset.");metadata=Convert.ToString(url);}}
   if(metadata==null)throw new InvalidDataException("This release has no update.json.");AssetUrl(metadata,".json");if(!new Uri(metadata).AbsolutePath.EndsWith("/update.json",StringComparison.Ordinal))throw new InvalidDataException("Unexpected update metadata URL.");return ReadRelease(metadata);
  });}
  HubRelease ReadRelease(string url){var release=UpdateEngine.Json().Deserialize<HubRelease>(DecodeJson(Download(url,1024*1024,true)));if(release==null)throw new InvalidDataException("Empty update metadata.");release.Validate();return new Version(release.Version)>new Version(ProductInfo.Version)?release:null;}
  public Task<PreparedHubUpdate> PrepareAsync(HubRelease release){return Task.Run(()=>{
   if(release==null)throw new ArgumentNullException("release");release.Validate();if(new Version(release.Version)<=new Version(ProductInfo.Version))throw new InvalidDataException("Update version is not newer.");string work=Path.Combine(dataRoot,"updates",Guid.NewGuid().ToString("N"));if(UpdateEngine.Under(appDirectory,work)||UpdateEngine.Under(work,appDirectory)||work.Equals(appDirectory,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update staging must be outside the application directory.");UpdateEngine.NoLinks(work);Directory.CreateDirectory(work);
   try{Notify(5,"Загрузка обновления");string archive=UpdateEngine.Inside(work,"package.zip");File.WriteAllBytes(archive,Download(release.PackageUrl,UpdateEngine.MaxArchive,true));if(!UpdateEngine.Hash(archive).Equals(release.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update ZIP SHA256 mismatch.");Notify(75,"Проверка файлов");string stage=UpdateEngine.Inside(work,"payload");UpdateEngine.Extract(archive,stage);var manifest=UpdateEngine.Manifest(stage,true);if(manifest.Version!=release.Version)throw new InvalidDataException("Update version differs from package.");using(var parent=Process.GetCurrentProcess()){var plan=new UpdatePlan{SchemaVersion=1,Target=appDirectory,Work=work,Stage=stage,ManifestHash=UpdateEngine.Hash(UpdateEngine.Inside(stage,"bundle-files.json")),Version=release.Version,ParentPid=parent.Id,ParentStartedUtc=parent.StartTime.ToUniversalTime().Ticks,ParentPath=parent.MainModule.FileName};UpdateEngine.ValidatePlan(plan);string path=UpdateEngine.Inside(work,"plan.json");File.WriteAllText(path,UpdateEngine.Json().Serialize(plan),new UTF8Encoding(false));Notify(100,"Обновление готово");return new PreparedHubUpdate{PlanPath=path};}}
   catch{try{Directory.Delete(work,true);}catch{}throw;}
  });}
  public void StartApply(PreparedHubUpdate prepared){if(prepared==null)throw new ArgumentNullException("prepared");var plan=UpdateEngine.LoadPlan(prepared.PlanPath);if(!plan.Target.Equals(appDirectory,StringComparison.OrdinalIgnoreCase)||!UpdateEngine.Under(Path.Combine(dataRoot,"updates"),plan.Work))throw new InvalidDataException("Prepared update belongs to another application.");using(var current=Process.GetCurrentProcess()){if(plan.ParentPid!=current.Id||plan.ParentStartedUtc!=current.StartTime.ToUniversalTime().Ticks||!plan.ParentPath.Equals(current.MainModule.FileName,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update parent changed.");}
   var installed=UpdateEngine.Manifest(appDirectory,false);var entry=installed.Files.Single(f=>f.Path.Equals("ReFix.UpdateHelper.exe",StringComparison.OrdinalIgnoreCase));string source=UpdateEngine.Inside(appDirectory,entry.Path);if(!UpdateEngine.Hash(source).Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Installed update helper verification failed.");string helper=UpdateEngine.Inside(plan.Work,"ReFix.UpdateHelper.exe");File.Copy(source,helper,false);if(!UpdateEngine.Hash(helper).Equals(entry.Sha256,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Copied update helper verification failed.");Process.Start(new ProcessStartInfo(helper,"--plan \""+prepared.PlanPath+"\""){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=plan.Work});
  }
  internal static void AssetUrl(string value,string suffix){Uri u;if(!Uri.TryCreate(value,UriKind.Absolute,out u)||u.Scheme!="https"||u.Host!="github.com"||u.Port!=443||u.UserInfo!=""||u.Query!=""||u.Fragment!=""||!Regex.IsMatch(u.AbsolutePath,"^/KirrieD/HUB-ReFIXwin11/releases/download/[A-Za-z0-9._-]+/[A-Za-z0-9._-]+$")||!u.AbsolutePath.EndsWith(suffix,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Update URL must be an official repository release asset.");}
  internal static void Redirect(Uri next){if(next.Scheme!="https"||next.Port!=443||next.UserInfo!=""||next.Fragment!=""||!(next.Host=="release-assets.githubusercontent.com"||next.Host=="objects.githubusercontent.com"||next.Host=="github.com"))throw new InvalidDataException("Unexpected GitHub asset redirect.");if(next.Host=="github.com")AssetUrl(next.AbsoluteUri,Path.GetExtension(next.AbsolutePath));}
  internal static string FailureMessage(Exception error){
   var web=error as WebException;if(web==null)return error.Message;
   var response=web.Response as HttpWebResponse;
   if(response!=null){int code=(int)response.StatusCode;response.Dispose();if(code==403||code==429)return "GitHub временно отказал в доступе (HTTP "+code+"). Повторите проверку позже.";if(code==404)return "Файл обновления отсутствует в официальном релизе (HTTP 404).";if(code>=500)return "Сервер GitHub временно недоступен (HTTP "+code+"). Повторите позже.";return "GitHub вернул HTTP "+code+". "+web.Message;}
   return "Не удалось связаться с GitHub. Проверьте подключение к Интернету и повторите позже. "+web.Message;
  }
  byte[] Download(string url,long max,bool asset){
   for(int attempt=0;;attempt++){
    try{return DownloadAttempt(url,max,asset);}
    catch(WebException error){
     var response=error.Response as HttpWebResponse;int delay=250*(attempt+1);
     bool retry=response==null&&(error.Status==WebExceptionStatus.Timeout||error.Status==WebExceptionStatus.ConnectFailure||error.Status==WebExceptionStatus.ConnectionClosed||error.Status==WebExceptionStatus.ReceiveFailure||error.Status==WebExceptionStatus.SendFailure);
     if(response!=null){int code=(int)response.StatusCode;retry=code==408||code==500||code==502||code==503||code==504;
      // Never immediately retry an API/asset rate limit or ignore a server cooldown.
      string after=response.Headers["Retry-After"];int seconds;
      if(!string.IsNullOrEmpty(after)){if(int.TryParse(after,out seconds)&&seconds>=0&&seconds<=2)delay=Math.Max(delay,seconds*1000);else retry=false;}
      if(response.Headers["X-RateLimit-Remaining"]=="0")retry=false;
     }
     if(attempt>=2||!retry)throw;
     if(response!=null)response.Dispose();Notify(0,"Временная ошибка сети. Повторная попытка…");Thread.Sleep(delay);
    }
   }
  }
  byte[] DownloadAttempt(string url,long max,bool asset){
   ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;Uri uri=new Uri(url);var timer=Stopwatch.StartNew();
   for(int redirects=0;redirects<5;redirects++){
    var request=createRequest(uri);request.AllowAutoRedirect=false;request.UserAgent="ReFIXHub/"+ProductInfo.Version;
    request.Accept=asset?"application/octet-stream":"application/vnd.github+json";request.Timeout=15000;request.ReadWriteTimeout=15000;
    using(var response=(HttpWebResponse)request.GetResponse()){
     if((int)response.StatusCode>=300&&(int)response.StatusCode<400){if(!asset||string.IsNullOrEmpty(response.Headers["Location"]))throw new InvalidDataException("Unexpected metadata redirect.");uri=new Uri(uri,response.Headers["Location"]);Redirect(uri);continue;}
     if(response.ContentLength>max)throw new InvalidDataException("Download too large.");
     using(var input=response.GetResponseStream())using(var output=new MemoryStream()){
      byte[] buffer=new byte[32768];int n;
      while((n=input.Read(buffer,0,buffer.Length))>0){if(timer.ElapsedMilliseconds>120000)throw new TimeoutException("Update download timed out.");if(output.Length+n>max)throw new InvalidDataException("Download too large.");output.Write(buffer,0,n);}return output.ToArray();
     }
    }
   }
   throw new IOException("Too many GitHub redirects.");
  }
 }
}
