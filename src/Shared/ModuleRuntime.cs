using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;

namespace ReFix.Shared
{
    public sealed class ModuleRuntime : IDisposable
    {
        readonly string id, stateFile, token=Guid.NewGuid().ToString("N");readonly Action show,stop,settings;readonly object gate=new object();
        NamedPipeServerStream server;Thread worker;FileStream ownership;bool disposed,started;string payload;
        static void Validate(string id){if(String.IsNullOrEmpty(id)||!Regex.IsMatch(id,"^[a-zA-Z0-9][a-zA-Z0-9_-]{0,63}$"))throw new ArgumentException("Invalid module ID","id");}
        public static string PipeName(string id){return PipeName(id,Paths.Root);}
        public static string PipeName(string id,string root){Validate(id);if(String.IsNullOrWhiteSpace(root))throw new ArgumentException("A data root is required","root");string normalized=Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar).ToUpperInvariant();string scope;using(var sha=SHA256.Create())scope=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(normalized))).Replace("-","").ToLowerInvariant();return "ReFIXHub."+WindowsIdentity.GetCurrent().User.Value+"."+scope+"."+id;}
        public ModuleRuntime(string id,Action show,Action stop):this(id,show,stop,null){}
        public ModuleRuntime(string id,Action show,Action stop,Action settings){this.settings=settings;Validate(id);if(show==null||stop==null)throw new ArgumentNullException("callbacks");this.id=id;this.show=show;this.stop=stop;Paths.Ensure();stateFile=Path.Combine(Paths.State,id+".json");}
        NamedPipeServerStream NewServer(){var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));return new NamedPipeServerStream(PipeName(id),PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security);}
        public void Start(){lock(gate){if(disposed)throw new ObjectDisposedException("ModuleRuntime");if(started)return;
            // An exclusive file handle prevents two processes from publishing/deleting each other's state.
            ownership=new FileStream(stateFile+".lock",FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);
            try{server=NewServer();using(Process process=Process.GetCurrentProcess()){payload=new JavaScriptSerializer().Serialize(new {id=id,state="ON",pid=process.Id,startedUtc=process.StartTime.ToUniversalTime().Ticks.ToString(CultureInfo.InvariantCulture),protocol=1,executablePath=process.MainModule.FileName,instance=token});}string temporary=stateFile+"."+token+".tmp";File.WriteAllText(temporary,payload);if(File.Exists(stateFile))File.Replace(temporary,stateFile,null);else File.Move(temporary,stateFile);started=true;worker=new Thread(Serve);worker.IsBackground=true;worker.Name="ReFIX module control";worker.Start();}catch{if(server!=null)server.Dispose();ownership.Dispose();ownership=null;throw;}
        }}
        string ReadLine(NamedPipeServerStream pipe){var bytes=new byte[1];var line=new StringBuilder();var deadline=Stopwatch.StartNew();while(line.Length<256){IAsyncResult pending=pipe.BeginRead(bytes,0,1,null,null);int count;using(pending.AsyncWaitHandle){if(!pending.AsyncWaitHandle.WaitOne(Math.Max(0,1500-(int)deadline.ElapsedMilliseconds)))throw new IOException("Command timeout");count=pipe.EndRead(pending);}if(count==0)return null;if(bytes[0]==10)return line.ToString().TrimEnd('\r');if(bytes[0]>127)throw new IOException("Commands must be ASCII");line.Append((char)bytes[0]);}throw new IOException("Command exceeds 256 bytes");}
        void Serve(){while(true){NamedPipeServerStream active;lock(gate){if(disposed)return;active=server;}try{active.WaitForConnection();string command=ReadLine(active);string response=command=="status"?payload:(command=="show"||command=="stop"||command=="settings"&&settings!=null?"{\"ok\":true}":"{\"ok\":false,\"error\":\"unknown command\"}");byte[] output=Encoding.UTF8.GetBytes(response+"\n");IAsyncResult writing=active.BeginWrite(output,0,output.Length,null,null);using(writing.AsyncWaitHandle){if(!writing.AsyncWaitHandle.WaitOne(1500))throw new IOException("Reply timeout");active.EndWrite(writing);}if(command=="show")show();else if(command=="stop")stop();else if(command=="settings"&&settings!=null)settings();}catch(IOException){}catch(ObjectDisposedException){}catch(InvalidOperationException){}finally{lock(gate){active.Dispose();if(!disposed)try{server=NewServer();}catch{disposed=true;}}}lock(gate){if(disposed)return;}}}
        public void Dispose(){Thread thread;lock(gate){if(disposed&&ownership==null)return;disposed=true;if(server!=null)server.Dispose();thread=worker;}if(thread!=null&&thread!=Thread.CurrentThread)thread.Join(2000);lock(gate){if(ownership!=null){try{if(File.Exists(stateFile)){var value=new JavaScriptSerializer().DeserializeObject(File.ReadAllText(stateFile)) as System.Collections.Generic.Dictionary<string,object>;if(value!=null&&value.ContainsKey("instance")&&(string)value["instance"]==token)File.Delete(stateFile);}}catch(IOException){}catch(ArgumentException){}finally{ownership.Dispose();ownership=null;}}}}
    }
}
