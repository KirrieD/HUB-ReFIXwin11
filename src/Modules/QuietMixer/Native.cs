using System;
using System.Runtime.InteropServices;
using System.Text;

namespace QuietMixer
{
    internal static class Native
    {
        internal const int ToggleMessage = 0x8001, ExitMessage = 0x8002;
        internal delegate IntPtr MouseProc(int code, IntPtr wParam, IntPtr lParam);
        internal delegate bool EnumProc(IntPtr hwnd, IntPtr data);
        [StructLayout(LayoutKind.Sequential)] internal struct Point { internal int X, Y; }
        [StructLayout(LayoutKind.Sequential)] internal struct Rect { internal int Left, Top, Right, Bottom; internal System.Windows.Rect Value { get { return new System.Windows.Rect(Left, Top, Math.Max(0, Right-Left), Math.Max(0, Bottom-Top)); } } }
        [StructLayout(LayoutKind.Sequential)] internal struct MouseData { internal Point Point; internal uint Mouse, Flags, Time; internal UIntPtr Extra; }
        [StructLayout(LayoutKind.Sequential)] internal struct MonitorInfo { internal int Size; internal Rect Monitor, Work; internal uint Flags; }
        [DllImport("user32.dll")] internal static extern IntPtr SetWindowsHookEx(int id, MouseProc proc, IntPtr module, uint thread);
        [DllImport("user32.dll")] internal static extern bool UnhookWindowsHookEx(IntPtr hook);
        [DllImport("user32.dll")] internal static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wp, IntPtr lp);
        [DllImport("kernel32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr GetModuleHandle(string name);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern IntPtr FindWindow(string cls, string title);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern int GetClassName(IntPtr hwnd, StringBuilder name, int max);
        [DllImport("user32.dll")] internal static extern bool EnumWindows(EnumProc proc, IntPtr data);
        [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);
        [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
        [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
        [DllImport("user32.dll")] internal static extern bool PostMessage(IntPtr hwnd, int msg, IntPtr wp, IntPtr lp);


        [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point point);
        [DllImport("user32.dll")] internal static extern IntPtr MonitorFromPoint(Point point, uint flags);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
        [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hwnd);
        [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
        [DllImport("user32.dll", CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
        [DllImport("dwmapi.dll")] internal static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
        [DllImport("user32.dll", EntryPoint="SetProcessDpiAwarenessContext")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
        internal static string ClassName(IntPtr hwnd) { StringBuilder b = new StringBuilder(256); GetClassName(hwnd,b,b.Capacity); return b.ToString(); }
        internal static bool IsTaskbar(IntPtr hwnd) { string c = ClassName(hwnd); return c == "Shell_TrayWnd" || c == "Shell_SecondaryTrayWnd"; }
        internal static System.Windows.Rect WorkArea(Point anchor)
        {
            MonitorInfo m = new MonitorInfo(); m.Size = Marshal.SizeOf(typeof(MonitorInfo));
            if (GetMonitorInfo(MonitorFromPoint(anchor, 2), ref m)) return m.Work.Value;
            return System.Windows.SystemParameters.WorkArea;
        }
    }
}
