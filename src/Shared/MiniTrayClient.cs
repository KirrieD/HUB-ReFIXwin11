using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace ReFix.Shared
{
    public sealed class MiniTrayClient : IDisposable
    {
        const int WorkMessage=0x8000+412;
        readonly string root,id;readonly NotifyIcon tray;readonly ThemeStore theme;readonly Action left;readonly Action<int,int> right;readonly bool fallbackVisible;
        readonly ClientWindow window;readonly FileSystemWatcher watcher;readonly MiniTrayEntry entry;
        readonly ConcurrentQueue<Action> work=new ConcurrentQueue<Action>();readonly int uiThread;
        readonly ContextMenuStrip menu;readonly bool ownMenu;readonly ToolStripMenuItem transferItem;
        Process hostProcess;string hostIdentity;Icon generated;readonly Icon originalIcon;volatile bool disposed;int queued;
        delegate IntPtr MouseHook(int code,IntPtr wparam,IntPtr lparam);
        readonly MouseHook mouseHook;IntPtr hook;Point dragStart;Size dragSize;System.Threading.Timer dragTimeout;
        [StructLayout(LayoutKind.Sequential)] struct MouseData {public int X,Y;public uint MouseDataValue,Flags,Time;public UIntPtr Extra;}
        [StructLayout(LayoutKind.Sequential)] struct NativeRect {public int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")] static extern bool PostMessage(IntPtr hwnd,int msg,IntPtr wparam,IntPtr lparam);
        [DllImport("user32.dll")] static extern IntPtr SetWindowsHookEx(int type,MouseHook proc,IntPtr module,uint thread);
        [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(IntPtr handle);
        [DllImport("user32.dll")] static extern IntPtr CallNextHookEx(IntPtr handle,int code,IntPtr wparam,IntPtr lparam);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd,out NativeRect rect);
        [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
        sealed class ClientWindow : NativeWindow,IDisposable
        {
            readonly MiniTrayClient owner;
            public ClientWindow(MiniTrayClient owner){this.owner=owner;CreateHandle(new CreateParams{Caption="ReFIX Mini Tray client",Parent=new IntPtr(-3)});}
            protected override void WndProc(ref Message message)
            {
                if(message.Msg==WorkMessage){owner.Drain();return;}
                if(!owner.disposed&&message.Msg==MiniTrayFiles.LeftMessage){if(owner.left!=null)owner.left();return;}
                if(!owner.disposed&&message.Msg==MiniTrayFiles.RightMessage){if(owner.right!=null)owner.right(unchecked((int)message.WParam.ToInt64()),unchecked((int)message.LParam.ToInt64()));return;}
                base.WndProc(ref message);
            }
            public void Dispose(){DestroyHandle();}
        }
        public MiniTrayClient(string root,string id,string name,string version,NotifyIcon tray,Action left,Action<int,int> right,ThemeStore theme,bool fallbackVisible=true)
        {
            if(tray==null)throw new ArgumentNullException("tray");if(!MiniTrayFiles.ValidId(id))throw new ArgumentException("Invalid tray ID");
            this.root=Path.GetFullPath(root);this.id=id;this.tray=tray;this.left=left;this.right=right;this.theme=theme;this.fallbackVisible=fallbackVisible;originalIcon=tray.Icon;uiThread=Thread.CurrentThread.ManagedThreadId;
            mouseHook=OnGlobalMouse;
            window=new ClientWindow(this);
            try {
                entry=MiniTrayFiles.CreateEntry(id,name,version,window.Handle);MiniTrayFiles.WriteEntry(this.root,entry);
                string directory=MiniTrayFiles.TrayDirectory(this.root);watcher=new FileSystemWatcher(directory,"host.json");watcher.NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size;
                watcher.Changed+=OnHostFile;watcher.Created+=OnHostFile;watcher.Deleted+=OnHostFile;watcher.Renamed+=OnHostFile;watcher.Error+=OnWatcherError;watcher.EnableRaisingEvents=true;
                menu=tray.ContextMenuStrip;ownMenu=menu==null;if(ownMenu){menu=new ContextMenuStrip();tray.ContextMenuStrip=menu;}
                transferItem=new ToolStripMenuItem("Перенести в мини-меню");transferItem.Click+=OnTransfer;menu.Items.Add(transferItem);menu.Opening+=OnMenuOpening;tray.MouseDown+=OnTrayMouseDown;
                if(theme!=null)theme.Changed+=OnTheme;UpdateIcon();RefreshHost();
            }catch {Dispose();throw;}
        }
        void Queue(Action action)
        {
            if(disposed)return;work.Enqueue(action);if(Interlocked.Exchange(ref queued,1)==0)PostMessage(window.Handle,WorkMessage,IntPtr.Zero,IntPtr.Zero);
        }
        void Drain()
        {
            Interlocked.Exchange(ref queued,0);Action action;while(work.TryDequeue(out action))if(!disposed)action();
        }
        void OnHostFile(object sender,FileSystemEventArgs e){Queue(RefreshHost);}
        void OnWatcherError(object sender,ErrorEventArgs e){Queue(RefreshHost);}
        void OnHostExit(object sender,EventArgs e){Queue(RefreshHost);}
        void OnTheme(object sender,EventArgs e){Queue(UpdateIcon);}
        void UpdateIcon()
        {
            Icon next=null;try{ThemeProfile profile=theme==null?new ThemeProfile():theme.Current.Clone();profile.Normalize();next=ReFixIcons.CreateIcon(id,32,profile);tray.Icon=next;Icon old=generated;generated=next;next=null;if(old!=null)old.Dispose();entry.IconTheme=profile;MiniTrayFiles.WriteEntry(root,entry);}catch{if(next!=null)next.Dispose();}
        }
        void DetachHost(){if(hostProcess!=null){hostProcess.Exited-=OnHostExit;hostProcess.Dispose();hostProcess=null;}hostIdentity=null;}
        void RefreshHost()
        {
            MiniTrayHost host=MiniTrayFiles.ReadHost(root);string identity=host==null?null:host.Pid+":"+host.StartedUtc+":"+host.WindowHandle;
            if(identity!=hostIdentity){DetachHost();if(host!=null)try{
                hostProcess=Process.GetProcessById(host.Pid);hostProcess.Exited+=OnHostExit;hostProcess.EnableRaisingEvents=true;hostIdentity=identity;
                // Exit can race between verification and subscribing.
                if(hostProcess.HasExited){DetachHost();host=null;}
            }catch{DetachHost();host=null;}}
            tray.Visible=fallbackVisible&&(host==null||!host.HiddenIds.Contains(id));
            if(transferItem!=null){transferItem.Enabled=host!=null;transferItem.Text=host!=null&&host.HiddenIds.Contains(id)?"Вернуть в системный трей":"Перенести в мини-меню";}
        }
        void OnMenuOpening(object sender,CancelEventArgs e){RefreshHost();}
        void OnTransfer(object sender,EventArgs e){MiniTrayHost host=MiniTrayFiles.ReadHost(root);if(host!=null)MiniTrayFiles.RequestTransfer(entry,host,host.HiddenIds.Contains(id));}
        void OnTrayMouseDown(object sender,MouseEventArgs e)
        {
            if(e.Button!=MouseButtons.Left||!tray.Visible||MiniTrayFiles.ReadHost(root)==null)return;
            EndDrag();dragStart=Cursor.Position;dragSize=SystemInformation.DragSize;
            hook=SetWindowsHookEx(14,mouseHook,GetModuleHandle(null),0);
            if(hook!=IntPtr.Zero)dragTimeout=new System.Threading.Timer(delegate{Queue(EndDrag);},null,15000,Timeout.Infinite);
        }
        IntPtr OnGlobalMouse(int code,IntPtr wparam,IntPtr lparam)
        {
            try{if(code>=0&&wparam.ToInt32()==0x202){var data=(MouseData)Marshal.PtrToStructure(lparam,typeof(MouseData));Point end=new Point(data.X,data.Y),start=dragStart;Size size=dragSize;EndDrag();Queue(delegate{Drop(start,end,size);});}}catch{EndDrag();}
            return CallNextHookEx(IntPtr.Zero,code,wparam,lparam);
        }
        void EndDrag(){if(hook!=IntPtr.Zero){UnhookWindowsHookEx(hook);hook=IntPtr.Zero;}if(dragTimeout!=null){dragTimeout.Dispose();dragTimeout=null;}}
        public static bool DragHit(Rectangle target,Point start,Point end,Size threshold)
        {
            return target.Contains(end)&&(Math.Abs((long)end.X-start.X)>Math.Max(1,threshold.Width/2)||Math.Abs((long)end.Y-start.Y)>Math.Max(1,threshold.Height/2));
        }
        bool HitWindow(MiniTrayHost host,IntPtr hwnd,Point start,Point end,Size size)
        {
            NativeRect rect;return MiniTrayFiles.WindowBelongsTo(hwnd,host.Pid)&&GetWindowRect(hwnd,out rect)&&DragHit(Rectangle.FromLTRB(rect.Left,rect.Top,rect.Right,rect.Bottom),start,end,size);
        }
        void Drop(Point start,Point end,Size size)
        {
            MiniTrayHost host=MiniTrayFiles.ReadHost(root);if(host==null)return;
            long popupValue;IntPtr popup=IntPtr.Zero;if(Int64.TryParse(host.PopupWindowHandle,out popupValue)&&(IntPtr.Size==8||(popupValue>=Int32.MinValue&&popupValue<=Int32.MaxValue)))popup=new IntPtr(popupValue);
            if(HitWindow(host,MiniTrayFiles.Handle(host),start,end,size)||HitWindow(host,popup,start,end,size))MiniTrayFiles.RequestTransfer(entry,host,false);
        }
        public void Dispose()
        {
            if(disposed)return;if(Thread.CurrentThread.ManagedThreadId!=uiThread)throw new InvalidOperationException("Dispose the tray client on its owning UI thread");disposed=true;
            EndDrag();tray.MouseDown-=OnTrayMouseDown;if(menu!=null){menu.Opening-=OnMenuOpening;if(transferItem!=null){transferItem.Click-=OnTransfer;menu.Items.Remove(transferItem);transferItem.Dispose();}if(ownMenu){if(tray.ContextMenuStrip==menu)tray.ContextMenuStrip=null;menu.Dispose();}}
            if(theme!=null)theme.Changed-=OnTheme;if(watcher!=null){watcher.EnableRaisingEvents=false;watcher.Dispose();}DetachHost();
            if(entry!=null)MiniTrayFiles.RemoveEntry(root,entry);tray.Visible=fallbackVisible;
            if(generated!=null){if(Object.ReferenceEquals(tray.Icon,generated))tray.Icon=originalIcon;generated.Dispose();generated=null;}window.Dispose();Action action;while(work.TryDequeue(out action)){}
        }
    }
}
