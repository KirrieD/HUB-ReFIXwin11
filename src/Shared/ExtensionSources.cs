using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Web.Script.Serialization;
namespace ReFix.Shared {
 public sealed class RepositorySource {
  public string Repository {get;private set;} public string CatalogUrl {get;private set;}
  public RepositorySource(string repository,string catalogUrl){Repository=SourcePolicy.Repository(repository);CatalogUrl=string.IsNullOrWhiteSpace(catalogUrl)?"":SourcePolicy.Https(catalogUrl).AbsoluteUri;}
  public override string ToString(){return Repository+(CatalogUrl.Length==0?"":" → "+CatalogUrl);}
 }
 public sealed class SourceSnapshot {
  public ReadOnlyCollection<string> Folders {get;private set;} public ReadOnlyCollection<RepositorySource> Repositories {get;private set;}
  static string NormalizeFolder(string p){if(string.IsNullOrWhiteSpace(p)||!Path.IsPathRooted(p))throw new InvalidDataException("A full folder path is required.");string full=Path.GetFullPath(p);return full.Length>Path.GetPathRoot(full).Length?full.TrimEnd(Path.DirectorySeparatorChar):full;}
  public SourceSnapshot(IEnumerable<string> folders,IEnumerable<RepositorySource> repositories){Folders=folders.Select(NormalizeFolder).Distinct(StringComparer.OrdinalIgnoreCase).ToList().AsReadOnly();Repositories=repositories.GroupBy(r=>r.Repository,StringComparer.OrdinalIgnoreCase).Select(g=>g.First()).ToList().AsReadOnly();}
 }
 public sealed class SharedSourceStore {
  public const string OfficialRepository="https://github.com/KirrieD/HUB-ReFIXwin11";
  readonly string path;
  public SharedSourceStore(string root){path=Path.Combine(Path.GetFullPath(root),"sources.json");}
  public sealed class Document {public int SchemaVersion{get;set;} public List<string> Folders{get;set;} public List<Entry> Repositories{get;set;}}
  public sealed class Entry {public string Repository{get;set;} public string CatalogUrl{get;set;}}
  public SourceSnapshot Read(){if(!File.Exists(path))return new SourceSnapshot(new string[0],new RepositorySource[0]);if(new FileInfo(path).Length>1024*1024)throw new InvalidDataException("Source profile too large.");var d=new JavaScriptSerializer().Deserialize<Document>(File.ReadAllText(path));if(d==null||d.SchemaVersion!=1||d.Folders==null||d.Repositories==null)throw new InvalidDataException("Invalid source profile.");return new SourceSnapshot(d.Folders,d.Repositories.Select(r=>new RepositorySource(r.Repository,r.CatalogUrl)));}
  public void Save(SourceSnapshot snapshot){Directory.CreateDirectory(Path.GetDirectoryName(path));var d=new Document{SchemaVersion=1,Folders=snapshot.Folders.ToList(),Repositories=snapshot.Repositories.Select(r=>new Entry{Repository=r.Repository,CatalogUrl=r.CatalogUrl}).ToList()};string text=new JavaScriptSerializer().Serialize(d);if(text.Length>1024*1024)throw new InvalidDataException("Source profile too large.");string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,text,new UTF8Encoding(false));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}finally{if(File.Exists(temp))File.Delete(temp);}}
 }
 public static class SourcePolicy {
  public static Uri Https(string value){Uri u;if(string.IsNullOrWhiteSpace(value)||value.Contains("\\")||value.Contains("#")||!Uri.TryCreate(value,UriKind.Absolute,out u)||u.Scheme!="https"||u.Port!=443||u.UserInfo!=""||u.Fragment!="")throw new InvalidDataException("A plain HTTPS URL is required.");string raw=value.Split('?')[0];foreach(string p in raw.Substring(raw.IndexOf("://",StringComparison.Ordinal)+3).Split('/')){string s=Uri.UnescapeDataString(p);if(s=="."||s==".."||s.Contains("/")||s.Contains("\\"))throw new InvalidDataException("Unsafe URL path.");}return u;}
  public static string Repository(string value){var u=Https((value??"").Trim());if(u.Query!=""||u.AbsolutePath.Trim('/').Split('/').Length<2)throw new InvalidDataException("Enter the full repository URL.");string result=u.GetLeftPart(UriPartial.Path).TrimEnd('/');if(result.EndsWith(".git",StringComparison.OrdinalIgnoreCase))result=result.Substring(0,result.Length-4);return result;}
  public static string[] Catalogs(RepositorySource source){if(source.CatalogUrl!="")return new[]{source.CatalogUrl};var u=new Uri(source.Repository);string p=u.AbsolutePath;if(u.Host=="github.com")return new[]{"https://raw.githubusercontent.com"+p+"/main/catalog/catalog.json","https://raw.githubusercontent.com"+p+"/master/catalog/catalog.json"};if(u.Host=="gitlab.com")return new[]{source.Repository+"/-/raw/main/catalog/catalog.json",source.Repository+"/-/raw/master/catalog/catalog.json"};return new[]{source.Repository+"/raw/branch/main/catalog/catalog.json",source.Repository+"/raw/branch/master/catalog/catalog.json"};}
  public static void Package(string value,string repository){var u=Https(value);var r=new Uri(Repository(repository));if(u.Host!=r.Host||u.Query!=""||!u.AbsolutePath.StartsWith(r.AbsolutePath.TrimEnd('/')+"/",StringComparison.Ordinal)||!u.AbsolutePath.EndsWith(".zip",StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("Package must be an HTTPS ZIP inside its configured repository.");}
  public static void Redirect(string initial,string next,bool package){var a=Https(initial);var b=Https(next);if(a.Host==b.Host)return;if(package&&a.Host=="github.com"&&(b.Host=="release-assets.githubusercontent.com"||b.Host=="objects.githubusercontent.com"))return;throw new InvalidDataException("Unexpected source redirect.");}
 }
}
