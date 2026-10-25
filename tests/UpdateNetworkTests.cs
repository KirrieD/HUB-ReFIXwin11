using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using ReFix.Hub;
using ReFix.Updating;
internal static class UpdateNetworkTests {
 internal static void Run(Action<string,bool> report) {
  Test(report,"API forbidden falls back through official latest asset redirect",()=>{
   int api=0;var service=Service(uri=>{
    if(uri.Host=="api.github.com"){api++;return Response(403,"Forbidden", "", "Retry-After: 120\r\n");}
    if(uri.AbsolutePath=="/KirrieD/HUB-ReFIXwin11/releases/latest/download/update.json")return Response(302,"Found","","Location: "+Metadata+"\r\n");
    if(uri.AbsoluteUri==Metadata)return Response(200,"OK",Release(),"");throw new Exception("Unexpected URL "+uri);
   });
   if(service.CheckAsync().GetAwaiter().GetResult().Version!="99.0.0"||api!=1)throw new Exception("Official fallback did not recover forbidden API");
  });
  Test(report,"temporary server failure retries and uses asset download media type",()=>{
   int api=0;var service=Service(uri=>{if(uri.Host=="api.github.com")return ++api==1?Response(503,"Unavailable","",""):Response(200,"OK",Assets(),"");return Response(200,"OK",Release(),"");});
   if(service.CheckAsync().GetAwaiter().GetResult().Version!="99.0.0"||api!=2)throw new Exception("Transient response not recovered");
  });
  Test(report,"server failure has bounded attempts",()=>{int count=0;var service=Service(uri=>{count++;return Response(503,"Unavailable","","");});Reject(()=>service.CheckAsync().GetAwaiter().GetResult());if(count!=3)throw new Exception("Expected three attempts, got "+count);});
  Test(report,"API rate limit does not immediately repeat rate limited request",()=>{int api=0;var service=Service(uri=>{if(uri.Host=="api.github.com"){api++;return Response(429,"Too Many Requests","","Retry-After: 120\r\n");}return Response(200,"OK",Release(),"");});if(service.CheckAsync().GetAwaiter().GetResult().Version!="99.0.0"||api!=1)throw new Exception("Rate limited API was repeated");});
  Test(report,"network fallback still rejects invalid metadata hash",()=>{var service=Service(uri=>uri.Host=="api.github.com"?Response(403,"Forbidden","",""):Response(200,"OK",Release().Replace(new string('a',64),"bad"),""));Reject(()=>service.CheckAsync().GetAwaiter().GetResult(),typeof(InvalidDataException));});
  Test(report,"long server cooldown prevents automatic retries",()=>{int count=0;var service=Service(uri=>{count++;return Response(503,"Unavailable","","Retry-After: 120\r\n");});Reject(()=>service.CheckAsync().GetAwaiter().GetResult());if(count!=1)throw new Exception("Server cooldown ignored");});
  Test(report,"missing latest API release remains no update",()=>{int count=0;var service=Service(uri=>{count++;return Response(404,"Not Found","","");});if(service.CheckAsync().GetAwaiter().GetResult()!=null||count!=1)throw new Exception("Missing release unexpectedly retried");});
  Test(report,"fallback rejects unrelated redirect before connecting",()=>{int count=0;var service=Service(uri=>{count++;return uri.Host=="api.github.com"?Response(403,"Forbidden","",""):Response(302,"Found","","Location: https://evil.example/update.json\r\n");});Reject(()=>service.CheckAsync().GetAwaiter().GetResult());if(count!=2)throw new Exception("Unrelated redirect followed");});
  Test(report,"fallback preserves metadata size limit",()=>{var service=Service(uri=>uri.Host=="api.github.com"?Response(403,"Forbidden","",""):Response(200,"OK",new string('x',1024*1024+1),""));Reject(()=>service.CheckAsync().GetAwaiter().GetResult(),typeof(InvalidDataException));});
  Test(report,"unsafe API asset URL is rejected without fallback",()=>{int count=0;var service=Service(uri=>{count++;return Response(200,"OK",Assets().Replace(Metadata,"https://evil.example/update.json"),"");});Reject(()=>service.CheckAsync().GetAwaiter().GetResult());if(count!=1)throw new Exception("Unsafe metadata was downloaded");});
 }
 const string Metadata="https://github.com/KirrieD/HUB-ReFIXwin11/releases/download/v99/update.json";
 static string Assets(){return "{\"assets\":[{\"name\":\"update.json\",\"browser_download_url\":\""+Metadata+"\"}]}";}
 static string Release(){return UpdateEngine.Json().Serialize(new HubRelease{SchemaVersion=1,Version="99.0.0",PackageUrl="https://github.com/KirrieD/HUB-ReFIXwin11/releases/download/v99/app.zip",Sha256=new string('a',64),SetupUrl="https://github.com/KirrieD/HUB-ReFIXwin11/releases/download/v99/Setup.exe",SetupSha256=new string('a',64)});}
 static HubUpdateService Service(Func<Uri,string> respond){
  Func<Uri,HttpWebRequest> create=uri=>{
   var listener=new TcpListener(IPAddress.Loopback,0);listener.Start();int port=((IPEndPoint)listener.LocalEndpoint).Port;
   Task.Run(()=>{try{using(var client=listener.AcceptTcpClient())using(var stream=client.GetStream())using(var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true)){string line,accept="";while(!string.IsNullOrEmpty(line=reader.ReadLine()))if(line.StartsWith("Accept:",StringComparison.OrdinalIgnoreCase))accept=line.Substring(7).Trim();string response=uri.Host!="api.github.com"&&accept!="application/octet-stream"?Response(406,"Not Acceptable","Incorrect asset Accept header",""):respond(uri);byte[] bytes=Encoding.UTF8.GetBytes(response);stream.Write(bytes,0,bytes.Length);}}finally{listener.Stop();}});
   var request=(HttpWebRequest)WebRequest.Create("http://127.0.0.1:"+port+"/");request.Proxy=null;return request;
  };
  return new HubUpdateService(Path.GetFullPath("artifacts/network-data"),Path.GetFullPath("artifacts/network-app"),create);
 }
 static string Response(int status,string reason,string body,string headers){return "HTTP/1.1 "+status+" "+reason+"\r\nContent-Length: "+Encoding.UTF8.GetByteCount(body)+"\r\nConnection: close\r\n"+headers+"\r\n"+body;}
 static void Reject(Action action,Type expected=null){try{action();}catch(Exception ex){if(expected!=null&&!expected.IsInstanceOfType(ex))throw;return;}throw new Exception("Unsafe or unavailable update accepted");}
 static void Test(Action<string,bool> report,string name,Action action){try{action();report(name,true);}catch(Exception ex){Console.WriteLine(ex);report(name,false);}}
}
