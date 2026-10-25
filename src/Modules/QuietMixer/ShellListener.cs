using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Automation;

namespace QuietMixer
{
    internal sealed class ShellSnapshot
    {
        internal Rect[] Taskbars = new Rect[0], Speakers = new Rect[0];
        internal DateTime Time = DateTime.MinValue;
    }
    internal sealed class ShellListener : IDisposable
    {
        private readonly AutoResetEvent refresh = new AutoResetEvent(false);
        private readonly Thread worker;
        private volatile bool stopped;
        private volatile ShellSnapshot snapshot = new ShellSnapshot();
        private readonly Native.MouseProc callback;
        private readonly IntPtr window;
        private IntPtr hook;
        private bool inTaskbar, suppressUp;
        private DateTime lastRequest=DateTime.MinValue;
        internal bool HookAvailable { get { return hook != IntPtr.Zero; } }
        internal ShellListener(IntPtr hwnd)
        {
            window = hwnd;
            callback = OnMouse;
            worker = new Thread(Work); worker.IsBackground = true; worker.Name = "QuietMixer shell discovery";
            worker.SetApartmentState(ApartmentState.MTA); worker.Start();
            hook = Native.SetWindowsHookEx(14, callback, Native.GetModuleHandle(null), 0);
            Refresh();
        }
        internal void Refresh() { if (!stopped) refresh.Set(); }
        private void Work()
        {
            while (true)
            {
                refresh.WaitOne(); if (stopped) return;
                try { snapshot = Discover(null); }
                catch (Exception ex) { if (!(ex is ElementNotAvailableException || ex is COMException || ex is InvalidOperationException)) Diagnostics.Log(ex); }
            }
        }
        internal static ShellSnapshot Discover(TextWriter trace)
        {
            List<IntPtr> handles = new List<IntPtr>();
            Native.EnumWindows(delegate(IntPtr hwnd, IntPtr arg) { if (Native.IsTaskbar(hwnd)) handles.Add(hwnd); return true; }, IntPtr.Zero);
            List<Rect> bars = new List<Rect>(), speakers = new List<Rect>();
            foreach (IntPtr hwnd in handles)
            {
                Native.Rect r; Native.GetWindowRect(hwnd, out r); bars.Add(r.Value);
                if (trace != null) trace.WriteLine("TASKBAR " + hwnd + " " + r.Value);
                AutomationElement root = AutomationElement.FromHandle(hwnd);
                AutomationElementCollection elements = root.FindAll(TreeScope.Descendants, System.Windows.Automation.Condition.TrueCondition);
                for (int i = 0; i < elements.Count; i++)
                {
                    try
                    {
                        AutomationElement.AutomationElementInformation c = elements[i].Current;
                        Rect rect = c.BoundingRectangle;
                        if (trace != null) trace.WriteLine(c.ControlType.ProgrammaticName + " | " + c.AutomationId + " | " + c.Name + " | " + rect);
                        if (!c.IsOffscreen && !rect.IsEmpty && Policy.IsVolumeIcon(c.AutomationId, c.Name, rect.Width, rect.Height) && r.Value.IntersectsWith(rect)) speakers.Add(rect);
                    }
                    catch (ElementNotAvailableException) { }
                }
            }
            return new ShellSnapshot { Taskbars = bars.ToArray(), Speakers = speakers.ToArray(), Time = DateTime.UtcNow };
        }
        private IntPtr OnMouse(int code, IntPtr wp, IntPtr lp)
        {
            if (code < 0) return Native.CallNextHookEx(hook, code, wp, lp);
            int message = wp.ToInt32();
            if (message != 0x200 && message != 0x201 && message != 0x202) return Native.CallNextHookEx(hook,code,wp,lp);
            Native.MouseData m = (Native.MouseData)Marshal.PtrToStructure(lp, typeof(Native.MouseData));
            ShellSnapshot s = snapshot;
            bool inside = false;
            foreach (Rect bar in s.Taskbars) if (Policy.Hit(bar,m.Point.X,m.Point.Y)) { inside = true; break; }
            if (inside && (!inTaskbar || (DateTime.UtcNow-s.Time).TotalSeconds>2) && (DateTime.UtcNow-lastRequest).TotalSeconds>2)
            { lastRequest=DateTime.UtcNow; Refresh(); }
            inTaskbar = inside;
            if (message == 0x202 && suppressUp) { suppressUp = false; return new IntPtr(1); }
            if (message == 0x201 && inside)
            {
                // API calls here are local, bounded hit tests only. Never call UIA in a hook.
                IntPtr root = Native.GetAncestor(Native.WindowFromPoint(m.Point), 2);
                if (Native.IsTaskbar(root))
                {
                    foreach (Rect rect in s.Speakers)
                    {
                        if (!Policy.Hit(rect, m.Point.X, m.Point.Y)) continue;
                        suppressUp = true;
                        Native.PostMessage(window, Native.ToggleMessage, IntPtr.Zero, IntPtr.Zero);
                        return new IntPtr(1);
                    }
                }
                Refresh();
            }
            return Native.CallNextHookEx(hook, code, wp, lp);
        }
        public void Dispose()
        {
            if (hook != IntPtr.Zero) { Native.UnhookWindowsHookEx(hook); hook = IntPtr.Zero; }
            stopped = true; refresh.Set();
            // UIA providers can block; never block application exit waiting for one.
            if (worker.Join(100)) refresh.Dispose();
        }
    }
}
