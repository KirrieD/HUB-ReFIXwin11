using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Automation;

namespace ReFix.MiniIcons
{
    /// <summary>
    /// Opens Explorer's own hidden-icon popup, without transferring foreign icons.
    /// RefreshAsync only reads UIA. Call OpenWindowsOverflowAsync only from a user
    /// click. No keyboard injection, shell messages, registry edits or polling.
    /// UIA providers cannot be forcibly cancelled: callers wait at most 1200ms,
    /// and at most one provider worker may remain pending. Lookup timeouts forbid
    /// a later Invoke; an Invoke already dispatched can finish after the timeout.
    /// </summary>
    public sealed class ExternalWindowsOverflow : IDisposable
    {
        private readonly object gate = new object();
        private readonly Func<bool, Func<bool>, bool> operation;
        private readonly int timeout;
        private bool busy, disposed, canOpen;
        private string detail = "Системная область скрытых значков ещё не проверена.";
        public ExternalWindowsOverflow() : this(NativeOperation, 1200) { }
        internal ExternalWindowsOverflow(Func<bool, Func<bool>, bool> operation, int timeout)
        {
            this.operation = operation;
            this.timeout = timeout;
        }
        public bool CanOpenWindowsOverflow { get { lock (gate) return canOpen && !disposed; } }
        public string Detail { get { lock (gate) return detail; } }
        public Task<bool> RefreshAsync() { return RunAsync(false); }
        public Task<bool> OpenWindowsOverflowAsync() { return RunAsync(true); }

        private async Task<bool> RunAsync(bool invoke)
        {
            lock (gate)
            {
                if (disposed || busy) return false;
                busy = true;
            }
            bool expired = false;
            Stopwatch elapsed = Stopwatch.StartNew();
            Func<bool> permitted = delegate
            {
                lock (gate) return !disposed && !expired && elapsed.ElapsedMilliseconds < timeout;
            };
            Task<bool> worker = Task.Run(delegate
            {
                try { return operation(invoke, permitted); }
                catch (Exception) { return false; }
                finally { lock (gate) busy = false; }
            });
            Task done = await Task.WhenAny(worker, Task.Delay(timeout)).ConfigureAwait(false);
            bool success = done == worker && await worker.ConfigureAwait(false);

            lock (gate)
            {
                expired = true;
                canOpen = success;
                detail = success ? "Доступна системная область значков Windows." :
                    done != worker ? "Windows не ответила вовремя. Повторите открытие позже." :
                    "Не найдена кнопка скрытых значков Windows. Проверьте настройки области уведомлений панели задач.";
            }
            return success;
        }
        public void Dispose() { lock (gate) { disposed = true; canOpen = false; } }

        private static bool NativeOperation(bool invoke, Func<bool> permitted)
        {
            IntPtr tray = FindWindow("Shell_TrayWnd", null);
            if (tray == IntPtr.Zero || !permitted()) return false;
            uint pid;
            GetWindowThreadProcessId(tray, out pid);
            using (Process owner = Process.GetProcessById((int)pid))
                if (!String.Equals(owner.ProcessName, "explorer", StringComparison.OrdinalIgnoreCase)) return false;
            AutomationElement root = AutomationElement.FromHandle(tray);
            // Restrict lookup to Explorer's taskbar and exact accessible names.
            // AutomationIds vary by build; an unknown or missing provider fails closed.
            Condition names = new OrCondition(
                new PropertyCondition(AutomationElement.NameProperty, "Show hidden icons"),
                new PropertyCondition(AutomationElement.NameProperty, "Показать скрытые значки"),
                new PropertyCondition(AutomationElement.NameProperty, "Отображать скрытые значки"));
            Condition condition = new AndCondition(names,
                new PropertyCondition(AutomationElement.AutomationIdProperty, "SystemTrayIcon"),
                new PropertyCondition(AutomationElement.ClassNameProperty, "SystemTray.NormalButton"),
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Button),
                new PropertyCondition(AutomationElement.ProcessIdProperty, (int)pid));
            AutomationElementCollection buttons = root.FindAll(TreeScope.Descendants, condition);
            if (buttons.Count != 1 || !permitted()) return false;
            AutomationElement button = buttons[0];
            if (!button.Current.IsEnabled || button.Current.IsOffscreen) return false;
            object raw;
            if (!button.TryGetCurrentPattern(InvokePattern.Pattern, out raw) || !permitted()) return false;
            if (invoke) ((InvokePattern)raw).Invoke();
            return true;
        }
        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr FindWindow(string className, string windowName);
        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);
    }
}
