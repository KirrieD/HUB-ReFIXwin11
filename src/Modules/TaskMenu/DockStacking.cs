using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using ReFix.Shared;
namespace ReFix.TaskMenu
{
    internal static class DockStacking
    {
        static readonly uint own=LayerNative.GetCurrentProcessId();
        internal static bool IsDesktop(IntPtr window){var name=new StringBuilder(128);Native.GetClassName(window,name,name.Capacity);string kind=name.ToString();return kind=="Progman"||kind=="WorkerW"||kind=="#32769";}
        internal static bool FullscreenOnMonitor(Rect monitor,Rect foreground,bool maximized,Func<IntPtr,bool> excluded)
        {
            if(TaskMenuLayerPolicy.OnMonitor(monitor,foreground))return TaskMenuLayerPolicy.Fullscreen(monitor,foreground,maximized);
            // Focus may be on another monitor. Keep yielding to the front application here.
            bool fullscreen=false;
            Native.EnumWindows(delegate(IntPtr window,IntPtr unused){if(!LayerNative.IsWindowVisible(window)||LayerNative.IsIconic(window)||DockStacking.IsDesktop(window))return true;if(excluded(window))return true;
                int cloaked;if(LayerNative.DwmGetWindowAttribute(window,14,out cloaked,4)==0&&cloaked!=0)return true;
                var name=new StringBuilder(128);Native.GetClassName(window,name,name.Capacity);if(name.ToString()=="Shell_TrayWnd"||name.ToString()=="Shell_SecondaryTrayWnd")return true;
                Native.RECT r;if(!Native.GetWindowRect(window,out r)||!TaskMenuLayerPolicy.OnMonitor(monitor,r.ToRect()))return true;
                bool full=TaskMenuLayerPolicy.Fullscreen(monitor,r.ToRect(),LayerNative.IsZoomed(window));if(DockPresentation.IsToolWindow(window)&&!full)return true;
                fullscreen=full;return false;},IntPtr.Zero);
            return fullscreen;
        }
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
