using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace ReFix.Shared
{
    public static class CaptureVisibility
    {
        public static bool IsCaptureProcess(string name)
        {
            return String.Equals(name,"SnippingTool",StringComparison.OrdinalIgnoreCase)
                ||String.Equals(name,"ScreenClippingHost",StringComparison.OrdinalIgnoreCase)
                ||String.Equals(name,"ScreenSketch",StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsCaptureActive
        {
            get
            {
                if((Down(0x5B)||Down(0x5C))&&Down(0x10)&&Down(0x53))return true;
                uint pid;GetWindowThreadProcessId(GetForegroundWindow(),out pid);
                if(pid==0)return false;
                try {using(var process=Process.GetProcessById((int)pid))return IsCaptureProcess(process.ProcessName);}
                catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(System.ComponentModel.Win32Exception){return false;}
            }
        }
        static bool Down(int key){return (GetAsyncKeyState(key)&0x8000)!=0;}
        [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
        [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
        [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr hwnd,out uint pid);
    }
}
