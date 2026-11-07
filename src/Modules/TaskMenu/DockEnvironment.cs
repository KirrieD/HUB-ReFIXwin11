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
            if(disposed)return;bool capture=CaptureVisibility.IsCaptureActive;foreach(var dock in docks)dock.CaptureActivityChanged(capture);bool ownDock=false;foreach(var dock in docks)if(dock.NativeHandle==foreground)ownDock=true;
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
            return DockStacking.FullscreenOnMonitor(monitor,foreground,maximized,window=>System.Linq.Enumerable.Any(docks,dock=>dock.NativeHandle==window));
        }
        public void Dispose(){if(disposed)return;disposed=true;pending.Stop();source.RemoveHook(Message);foreach(var h in hooks)if(h!=IntPtr.Zero)Native.UnhookWinEvent(h);if(RawEnabled){var device=new LayerNative.RAWINPUTDEVICE{Page=1,Usage=2,Flags=1};LayerNative.RegisterRawInputDevices(new[]{device},1,(uint)Marshal.SizeOf(device));RawEnabled=false;}}
    }
}
