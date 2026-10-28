using System;
using System.Runtime.InteropServices;
namespace ReFix.TaskMenu
{
    internal static class DockPresentation
    {
        internal const uint ExcludeFromCapture=0x11;
        [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")]static extern IntPtr GetLong64(IntPtr window,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")]static extern IntPtr SetLong64(IntPtr window,int index,IntPtr value);
        [DllImport("user32.dll",EntryPoint="GetWindowLongW")]static extern int GetLong32(IntPtr window,int index);
        [DllImport("user32.dll",EntryPoint="SetWindowLongW")]static extern int SetLong32(IntPtr window,int index,int value);
        [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowDisplayAffinity(IntPtr window,uint affinity);
        [DllImport("user32.dll")]internal static extern bool GetWindowDisplayAffinity(IntPtr window,out uint affinity);
        [DllImport("dwmapi.dll")]internal static extern int DwmIsCompositionEnabled(out bool enabled);
        static long Style(IntPtr window){return IntPtr.Size==8?GetLong64(window,-20).ToInt64():GetLong32(window,-20);}
        internal static bool IsToolWindow(IntPtr window){long style=Style(window);return (style&0x80)!=0&&(style&0x40000)==0;}
        internal static bool Apply(IntPtr window)
        {
            long style=(Style(window)|0x80)&~0x40000L;
            if(IntPtr.Size==8)SetLong64(window,-20,new IntPtr(style));else SetLong32(window,-20,(int)style);
            try{return SetWindowDisplayAffinity(window,ExcludeFromCapture);}catch(EntryPointNotFoundException){return false;}
        }
    }
}
