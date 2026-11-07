using System;
using System.Threading;
using System.Windows;
using System.Windows.Interop;
using ReFix.Shared;

namespace ReFix.QuickNote
{
    internal static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            bool created;
            using (var mutex = new Mutex(true, "Local\\" + ModuleRuntime.PipeName("quick-note"), out created))
            {
                if (!created) return;
                Paths.Ensure();
                var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
                Library library;
                try { library = new Library(Paths.Root); }
                catch (Exception ex)
                {
                    MessageBox.Show("Не удалось открыть библиотеку заметок: " + ex.Message, "Быстрая заметка");
                    return;
                }

                using (var themes = new ThemeStore(Paths.Root))
                using (var controls = new ControlsStore(Paths.Root))
                using (var source = new HwndSource(new HwndSourceParameters(ModuleRuntime.PipeName("quick-note") + ".Keys") { Width = 0, Height = 0, WindowStyle = unchecked((int)0x80000000) }))
                using (var controller = new NoteController(library, themes, controls))
                using (var keys = new GlobalKeyRegistration(controls, "quick-note.toggle", source.Handle, 0x5200, controller.Toggle))
                using (var tray = new System.Windows.Forms.NotifyIcon { Text = "Быстрая заметка", Icon = System.Drawing.SystemIcons.Application, Visible = true })
                using (var menu = new System.Windows.Forms.ContextMenuStrip())
                {
                    Action stop = delegate { if (controller.SaveAll()) app.Shutdown(); };
                    source.AddHook(delegate(IntPtr h, int msg, IntPtr w, IntPtr l, ref bool handled) { handled = keys.ProcessMessage(msg, w); return IntPtr.Zero; });
                    menu.Items.Add("Показать списки", null, delegate { controller.Show(); });
                    menu.Items.Add("Новая заметка", null, delegate { controller.NewNote(library.Index.Lists[0]); });
                    menu.Items.Add("Выйти", null, delegate { stop(); });
                    tray.ContextMenuStrip = menu;
                    tray.MouseClick += delegate(object sender, System.Windows.Forms.MouseEventArgs e) { if (e.Button == System.Windows.Forms.MouseButtons.Left) controller.Show(); };
                    using (var miniTray = new MiniTrayClient(Paths.Root, "quick-note", "Быстрая заметка", ModuleVersions.QuickNote, tray, controller.Show, (x, y) => menu.Show(x, y), themes))
                    using (var runtime = new ModuleRuntime("quick-note", delegate { app.Dispatcher.BeginInvoke(new Action(controller.Show)); }, delegate { app.Dispatcher.BeginInvoke(stop); }))
                    {
                        app.SessionEnding += delegate(object sender, SessionEndingCancelEventArgs e) { e.Cancel = !controller.SaveAll(); };
                        runtime.Start();
                        if (Array.IndexOf(args, "--background") < 0) app.Dispatcher.BeginInvoke(new Action(controller.Show));
                        app.Run();
                    }
                }
            }
        }
    }
}
