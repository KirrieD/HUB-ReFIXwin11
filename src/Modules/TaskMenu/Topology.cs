using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ReFix.Shared;

namespace ReFix.TaskMenu
{
    internal sealed class MonitorInfo
    {
        internal string Id;internal Rect Bounds,Work;internal double Scale;internal HashSet<TaskMenuEdge> Forbidden=new HashSet<TaskMenuEdge>();
        public override string ToString(){return Id+" ("+(int)Bounds.Width+" × "+(int)Bounds.Height+")";}
    }
    internal sealed class Topology : IDisposable
    {
        readonly Dispatcher dispatcher;readonly DispatcherTimer pending;readonly Native.WinEvent callback;readonly IntPtr hook;readonly HwndSource source;readonly uint taskbarCreated;bool disposed;
        internal List<MonitorInfo> Monitors=new List<MonitorInfo>();internal event EventHandler Changed;
        internal Topology(HwndSource source){this.source=source;dispatcher=source.Dispatcher;pending=new DispatcherTimer(TimeSpan.FromMilliseconds(180),DispatcherPriority.Background,delegate{pending.Stop();Refresh();},dispatcher);pending.Stop();taskbarCreated=Native.RegisterWindowMessage("TaskbarCreated");source.AddHook(Message);callback=OnWindowEvent;hook=Native.SetWinEventHook(0x8000,0x800B,IntPtr.Zero,callback,0,0,0);Refresh();}
        void OnWindowEvent(IntPtr h,uint ev,IntPtr window,int obj,int child,uint thread,uint time){if(disposed||window==IntPtr.Zero||obj!=0||child!=0)return;var name=new StringBuilder(128);Native.GetClassName(window,name,name.Capacity);if(name.ToString()=="Shell_TrayWnd"||name.ToString()=="Shell_SecondaryTrayWnd")dispatcher.BeginInvoke(new Action(Schedule));}
        internal void Schedule(){if(disposed)return;pending.Stop();pending.Start();}
        IntPtr Message(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){if(msg==0x007E||msg==0x001A||msg==0x02E0||(uint)msg==taskbarCreated)Schedule();return IntPtr.Zero;}
        void Refresh(){if(disposed)return;var monitors=new List<MonitorInfo>();Native.EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,delegate(IntPtr h,IntPtr dc,ref Native.RECT rect,IntPtr data){var info=new Native.MONITORINFOEX();info.cbSize=Marshal.SizeOf(info);if(Native.GetMonitorInfo(h,ref info)){uint x=96,y=96;try{Native.GetDpiForMonitor(h,0,out x,out y);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}monitors.Add(new MonitorInfo{Id=info.szDevice,Bounds=info.rcMonitor.ToRect(),Work=info.rcWork.ToRect(),Scale=x/96.0});}return true;},IntPtr.Zero);
            Native.EnumWindows(delegate(IntPtr window,IntPtr data){var name=new StringBuilder(128);Native.GetClassName(window,name,name.Capacity);if(name.ToString()=="Shell_TrayWnd"||name.ToString()=="Shell_SecondaryTrayWnd"){Native.RECT r;if(Native.GetWindowRect(window,out r))foreach(var m in monitors){var edge=TaskMenuGeometry.TaskbarEdge(m.Bounds,r.ToRect());if(edge.HasValue)m.Forbidden.Add(edge.Value);}}return true;},IntPtr.Zero);
            Monitors=monitors;var changed=Changed;if(changed!=null)changed(this,EventArgs.Empty);
        }
        public void Dispose(){disposed=true;pending.Stop();source.RemoveHook(Message);if(hook!=IntPtr.Zero)Native.UnhookWinEvent(hook);}
    }
    internal static class Native
    {
        [StructLayout(LayoutKind.Sequential)]internal struct RECT{public int Left,Top,Right,Bottom;internal Rect ToRect(){return new Rect(Left,Top,Math.Max(0,Right-Left),Math.Max(0,Bottom-Top));}}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]internal struct MONITORINFOEX{public int cbSize;public RECT rcMonitor,rcWork;public uint flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string szDevice;}
        internal delegate bool MonitorEnum(IntPtr monitor,IntPtr dc,ref RECT rect,IntPtr data);
        internal delegate bool WindowEnum(IntPtr window,IntPtr data);
        internal delegate void WinEvent(IntPtr hook,uint ev,IntPtr window,int obj,int child,uint thread,uint time);
        [DllImport("user32.dll")]internal static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorEnum proc,IntPtr data);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern bool GetMonitorInfo(IntPtr monitor,ref MONITORINFOEX info);
        [DllImport("shcore.dll")]internal static extern int GetDpiForMonitor(IntPtr monitor,int type,out uint x,out uint y);
        [DllImport("user32.dll")]internal static extern bool EnumWindows(WindowEnum proc,IntPtr data);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern int GetClassName(IntPtr window,StringBuilder name,int count);
        [DllImport("user32.dll")]internal static extern bool GetWindowRect(IntPtr window,out RECT rect);
        [DllImport("user32.dll")]internal static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        [DllImport("user32.dll")]internal static extern IntPtr SetWinEventHook(uint min,uint max,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
        [DllImport("user32.dll")]internal static extern bool UnhookWinEvent(IntPtr hook);
        [DllImport("user32.dll",CharSet=CharSet.Unicode)]internal static extern uint RegisterWindowMessage(string name);
    }
}
