using System;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace ReFix.Shared
{
    public sealed class ThemeStore : IDisposable
    {
        readonly string root,file; readonly object gate=new object(); readonly FileSystemWatcher watcher;
        ThemeProfile current,pending; bool disposed,writeScheduled; int writeRetries; string lastError; readonly Timer saveTimer;
        public event EventHandler Changed;
        public ThemeProfile Current { get { lock(gate)return current.Clone(); } }
        public string LastError { get { lock(gate)return lastError; } }
        public ThemeStore(string root) {
            if(String.IsNullOrWhiteSpace(root))throw new ArgumentException("A data root is required","root");
            this.root=Path.GetFullPath(root);Directory.CreateDirectory(this.root);file=Path.Combine(this.root,"theme.json");current=Read()??new ThemeProfile();
            watcher=new FileSystemWatcher(this.root,"theme.json");watcher.NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.FileName|NotifyFilters.Size;watcher.Changed+=OnFile;watcher.Created+=OnFile;watcher.Renamed+=OnFile;watcher.EnableRaisingEvents=true;
            saveTimer=new Timer(WritePending,null,Timeout.Infinite,Timeout.Infinite); SystemEvents.UserPreferenceChanged+=OnPreference;
        }
        ThemeProfile Read() {
            try {
                using(var stream=new FileStream(file,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete)) {
                    if(stream.Length>65536)return null;
                    using(var reader=new StreamReader(stream,System.Text.Encoding.UTF8)) {
                        var text=new char[65537];int count=reader.ReadBlock(text,0,text.Length);if(count>65536)return null;
                        var p=new JavaScriptSerializer().Deserialize<ThemeProfile>(new string(text,0,count));if(p==null)return null;p.Normalize();return p;
                    }
                }
            }catch(IOException){return null;}catch(UnauthorizedAccessException){return null;}catch(ArgumentException){return null;}catch(InvalidOperationException){return null;}
        }
        void Raise() {EventHandler handler=Changed;if(handler!=null)ThreadPool.QueueUserWorkItem(delegate{lock(gate){if(disposed)return;}handler(this,EventArgs.Empty);});}
        void OnPreference(object sender,UserPreferenceChangedEventArgs e){Raise();}
        void OnFile(object sender,FileSystemEventArgs e) {lock(gate){if(disposed||pending!=null)return;ThemeProfile next=Read();if(next==null)return;string a=new JavaScriptSerializer().Serialize(current),b=new JavaScriptSerializer().Serialize(next);if(a==b)return;current=next;}Raise();}
        public void Save(ThemeProfile profile) {
            if(profile==null)throw new ArgumentNullException("profile");ThemeProfile p=profile.Clone();p.Normalize();
            lock(gate){if(disposed)throw new ObjectDisposedException("ThemeStore");current=p;pending=p;writeRetries=0;if(!writeScheduled){writeScheduled=true;saveTimer.Change(100,Timeout.Infinite);}}Raise();
        }
        void WritePending(object state) {
            bool notify=false;
            lock(gate){
                if(disposed||pending==null)return;writeScheduled=false;
                try{FlushPending();if(lastError!=null){lastError=null;notify=true;}writeRetries=0;}
                catch(IOException e){notify=RecordWriteFailure(e);}
                catch(UnauthorizedAccessException e){notify=RecordWriteFailure(e);}
            }
            if(notify)Raise();
        }
        bool RecordWriteFailure(Exception error) {
            bool notify=lastError!=error.Message;lastError=error.Message;
            // At most five one-shot retries after the initial attempt. New user edits reset the budget.
            if(writeRetries<5){writeRetries++;writeScheduled=true;saveTimer.Change(100,Timeout.Infinite);}
            return notify;
        }
        void FlushPending() {
            if(pending==null)return;string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllText(temporary,new JavaScriptSerializer().Serialize(pending));if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);pending=null;}finally{if(File.Exists(temporary))File.Delete(temporary);}
        }
        public string ImportBackground(string source) {
            lock(gate){if(disposed)throw new ObjectDisposedException("ThemeStore");}
            var info=new FileInfo(source);if(!info.Exists||info.Length==0||info.Length>20*1024*1024)throw new InvalidDataException("Image must be between 1 byte and 20 MiB.");
            byte[] bytes;using(var input=new FileStream(info.FullName,FileMode.Open,FileAccess.Read,FileShare.Read)){if(input.Length==0||input.Length>20*1024*1024)throw new InvalidDataException("Image exceeds 20 MiB.");bytes=new byte[(int)input.Length];int offset=0;while(offset<bytes.Length){int count=input.Read(bytes,offset,bytes.Length-offset);if(count==0)throw new EndOfStreamException();offset+=count;}}
            using(var stream=new MemoryStream(bytes,false)){BitmapDecoder decoder=BitmapDecoder.Create(stream,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.None);if(decoder.Frames.Count!=1)throw new InvalidDataException("Use a single-frame image.");var frame=decoder.Frames[0];if(frame.PixelWidth>8192||frame.PixelHeight>8192||(long)frame.PixelWidth*frame.PixelHeight>32000000)throw new InvalidDataException("Image dimensions exceed the supported limit.");}
            string assets=Path.Combine(root,"assets");Directory.CreateDirectory(assets);string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();string dest=Path.Combine(assets,hash+".img");if(!File.Exists(dest)){string temp=dest+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllBytes(temp,bytes);try{File.Move(temp,dest);}catch(IOException){if(!File.Exists(dest))throw;}}finally{if(File.Exists(temp))File.Delete(temp);}}return dest;
        }
        public void Dispose(){lock(gate){if(disposed)return;disposed=true;saveTimer.Dispose();watcher.EnableRaisingEvents=false;watcher.Dispose();SystemEvents.UserPreferenceChanged-=OnPreference;FlushPending();}}
    }
}
