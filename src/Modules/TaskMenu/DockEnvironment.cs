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
    // Native notifications, not idle polling. Raw Input observes the mouse without blocking it.
    internal sealed class DockEnvironment:IDisposable
    {
        readonly HwndSource source;readonly ControlsStore controls;readonly IList<DockWindow> docks;readonly DispatcherTimer pending;
        readonly Native.WinEvent callback;readonly List<IntPtr> hooks=new List<IntPtr>();bool disposed,pointerPending;IntPtr previousForeground;
        internal bool RawEnabled{get;private set;}
        internal bool EventTimerRunning{get{return pending.IsEnabled;}}
        internal DockEnvironment(HwndSource source,ControlsStore controls,IList<DockWindow> docks)
        {
            this.source=source;this.controls=controls;this.docks=docks;
            pending=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};pending.Tick+=delegate{pending.Stop();Refresh();};source.AddHook(Message);
            callback=OnWindowEvent;
            hooks.Add(Native.SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0));
            hooks.Add(Native.SetWinEventHook(0x16,0x17,IntPtr.Zero,callback,0,0,0));
            hooks.Add(Native.SetWinEventHook(0x8000,0x800B,IntPtr.Zero,callback,0,0,0));
        }
        void OnWindowEvent(IntPtr hook,uint ev,IntPtr window,int obj,int child,uint thread,uint time)
        {
            if(disposed||window==IntPtr.Zero||obj!=0||child!=0)return;
            if(ev>=0x8000&&DockStacking.IsDesktop(window))return;
            if(ev!=3)foreach(var dock in docks)if(window==dock.NativeHandle)return;
            if(ev!=3&&ev!=0x16&&ev!=0x17&&ev!=0x8000&&ev!=0x8001&&ev!=0x8002&&ev!=0x8003&&ev!=0x8004&&ev!=0x800B)return;
            if(!pending.IsEnabled)pending.Start();
        }
        IntPtr Message(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled)
        {
            if(msg==0x00FF&&RawEnabled&&!disposed&&!pointerPending){pointerPending=true;source.Dispatcher.BeginInvoke(DispatcherPriority.Input,new Action(delegate{pointerPending=false;if(!disposed){LayerNative.POINT p;if(LayerNative.GetCursorPos(out p))ObservePointer(new Point(p.X,p.Y));}}));}
            return IntPtr.Zero;
        }
        internal void ObservePointer(Point point){if(disposed)return;foreach(var dock in docks)dock.ObservePointer(point);}
        internal void Refresh()
        {
            if(disposed)return;
            RefreshForWindow(LayerNative.GetForegroundWindow());
        }
        internal void RefreshForWindow(IntPtr foreground)
        {
            if(disposed)return;bool ownDock=false;foreach(var dock in docks)if(dock.NativeHandle==foreground)ownDock=true;
            if(ownDock)foreground=previousForeground;else previousForeground=foreground;
            IntPtr owner=LayerNative.GetAncestor(foreground,3);if(owner!=IntPtr.Zero)foreground=owner;
            Native.RECT rect;bool visible=foreground!=IntPtr.Zero&&!DockStacking.IsDesktop(foreground)&&LayerNative.IsWindowVisible(foreground)&&!LayerNative.IsIconic(foreground)&&Native.GetWindowRect(foreground,out rect);
            Rect bounds=Rect.Empty;if(visible&&Native.GetWindowRect(foreground,out rect))bounds=rect.ToRect();
            bool maximized=visible&&LayerNative.IsZoomed(foreground);
            var profile=controls.Current;bool desktop=foreground!=IntPtr.Zero&&DockStacking.IsDesktop(foreground);
            foreach(var dock in docks)dock.ApplyLayer(profile.TaskMenuLayer==TaskMenuLayer.ExceptFullscreen&&(visible||desktop)&&FullscreenOnMonitor(dock.MonitorBounds,bounds,maximized));
            bool need=docks.Count>0&&(!profile.ShowTaskMenuEdges||profile.TaskMenuLayer!=TaskMenuLayer.AboveAll);
            if(need!=RawEnabled){var device=new LayerNative.RAWINPUTDEVICE{Page=1,Usage=2,Flags=need?0x100u:1u,Target=need?source.Handle:IntPtr.Zero};if(LayerNative.RegisterRawInputDevices(new[]{device},1,(uint)Marshal.SizeOf(device)))RawEnabled=need;else if(need)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Не удалось включить наведение на скрытые грани.");}
        }
        internal bool FullscreenOnMonitor(Rect monitor,Rect foreground,bool maximized)
        {
            if(TaskMenuLayerPolicy.OnMonitor(monitor,foreground))return TaskMenuLayerPolicy.Fullscreen(monitor,foreground,maximized);
            // Focus may be on another monitor. Keep yielding to the front application here.
            bool fullscreen=false;
            Native.EnumWindows(delegate(IntPtr window,IntPtr unused){if(!LayerNative.IsWindowVisible(window)||LayerNative.IsIconic(window)||DockStacking.IsDesktop(window))return true;foreach(var dock in docks)if(window==dock.NativeHandle)return true;
                int cloaked;if(LayerNative.DwmGetWindowAttribute(window,14,out cloaked,4)==0&&cloaked!=0)return true;
                var name=new StringBuilder(128);Native.GetClassName(window,name,name.Capacity);if(name.ToString()=="Shell_TrayWnd"||name.ToString()=="Shell_SecondaryTrayWnd")return true;
                Native.RECT r;if(!Native.GetWindowRect(window,out r)||!TaskMenuLayerPolicy.OnMonitor(monitor,r.ToRect()))return true;
                bool full=TaskMenuLayerPolicy.Fullscreen(monitor,r.ToRect(),LayerNative.IsZoomed(window));if(DockPresentation.IsToolWindow(window)&&!full)return true;
                fullscreen=full;return false;},IntPtr.Zero);
            return fullscreen;
        }
        public void Dispose(){if(disposed)return;disposed=true;pending.Stop();source.RemoveHook(Message);foreach(var h in hooks)if(h!=IntPtr.Zero)Native.UnhookWinEvent(h);if(RawEnabled){var device=new LayerNative.RAWINPUTDEVICE{Page=1,Usage=2,Flags=1};LayerNative.RegisterRawInputDevices(new[]{device},1,(uint)Marshal.SizeOf(device));RawEnabled=false;}}
    }
    internal static class DockStacking
    {
        static readonly uint own=LayerNative.GetCurrentProcessId();
        internal static bool IsDesktop(IntPtr window){var name=new StringBuilder(128);Native.GetClassName(window,name,name.Capacity);string kind=name.ToString();return kind=="Progman"||kind=="WorkerW"||kind=="#32769";}
        internal static void Apply(IntPtr window,bool topmost)
        {
            if(window==IntPtr.Zero)return;
            if(topmost){Native.SetWindowPos(window,new IntPtr(-1),0,0,0,0,0x13);return;}
            Native.SetWindowPos(window,new IntPtr(-2),0,0,0,0,0x13);
            // Stay above the wallpaper/desktop host, but below every visible normal window.
            IntPtr anchor=IntPtr.Zero;
            Native.EnumWindows(delegate(IntPtr candidate,IntPtr unused){if(candidate==window||!LayerNative.IsWindowVisible(candidate)||LayerNative.IsIconic(candidate)||DockPresentation.IsTopmost(candidate))return true;
                uint process;LayerNative.GetWindowThreadProcessId(candidate,out process);if(process==own&&DockPresentation.IsToolWindow(candidate))return true;
                int cloaked;if(LayerNative.DwmGetWindowAttribute(candidate,14,out cloaked,4)==0&&cloaked!=0)return true;
                var name=new StringBuilder(128);Native.GetClassName(candidate,name,name.Capacity);string kind=name.ToString();if(kind=="Progman"||kind=="WorkerW"||kind=="Shell_TrayWnd"||kind=="Shell_SecondaryTrayWnd")return true;
                anchor=candidate;return true;},IntPtr.Zero);
            if(anchor!=IntPtr.Zero)Native.SetWindowPos(window,anchor,0,0,0,0,0x13);
        }
    }
    internal static class LayerNative
    {
        [StructLayout(LayoutKind.Sequential)]internal struct POINT{internal int X,Y;}
        [StructLayout(LayoutKind.Sequential)]internal struct RAWINPUTDEVICE{internal ushort Page,Usage;internal uint Flags;internal IntPtr Target;}
        [DllImport("user32.dll")]internal static extern IntPtr GetForegroundWindow();
        [DllImport("kernel32.dll")]internal static extern uint GetCurrentProcessId();
        [DllImport("user32.dll")]internal static extern IntPtr GetShellWindow();
        [DllImport("dwmapi.dll")]internal static extern int DwmGetWindowAttribute(IntPtr window,uint attribute,out int value,uint size);
        [DllImport("user32.dll")]internal static extern IntPtr GetAncestor(IntPtr window,uint flags);
        [DllImport("user32.dll")]internal static extern bool IsWindowVisible(IntPtr window);
        [DllImport("user32.dll")]internal static extern bool IsIconic(IntPtr window);
        [DllImport("user32.dll")]internal static extern bool IsZoomed(IntPtr window);
        [DllImport("user32.dll")]internal static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
        [DllImport("user32.dll")]internal static extern bool GetCursorPos(out POINT point);
        [DllImport("user32.dll",SetLastError=true)]internal static extern bool RegisterRawInputDevices([In]RAWINPUTDEVICE[] devices,uint count,uint size);
    }
}
