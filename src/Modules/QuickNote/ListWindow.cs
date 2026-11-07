using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using ReFix.Modules;
using ReFix.Shared;
using Point = System.Windows.Point;

namespace ReFix.QuickNote
{
    internal sealed class ListWindow : Window, IDisposable
    {
        const string NoteFormat = NoteWorkspace.NoteFormat;
        readonly NoteList list;
        readonly Library library;
        readonly NoteController controller;
        readonly ThemedSurface surface;
        readonly Button toggle;
        readonly StackPanel root;
        readonly TextBlock title;
        bool disposed;

        internal ListWindow(NoteList list, Library library, NoteController controller, ThemeProfile theme)
        {
            this.list = list; this.library = library; this.controller = controller;
            Width = 280; SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None; AllowsTransparency = true; ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false; ShowActivated = false; Background = Brushes.Transparent;
            ThemeControls.Install(this);
            root = new StackPanel { Margin = new Thickness(10), AllowDrop = true };
            var header = new DockPanel { Background = Brushes.Transparent };
            toggle = NoteEditor.Button("›", () => controller.OpenList(list));
            toggle.Margin = new Thickness(0);
            toggle.Padding = new Thickness(5, 0, 5, 0);
            toggle.MinWidth = 20;
            DockPanel.SetDock(toggle, Dock.Right);
            header.Children.Add(toggle);
            title = new TextBlock { FontSize = 15, Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.SizeAll };
            title.ToolTip = "Перетащить список";
            Point dragStart = new Point();
            title.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                dragStart=e.GetPosition(title); title.CaptureMouse(); e.Handled=true;
            };
            title.MouseLeftButtonUp += delegate(object sender, MouseButtonEventArgs e)
            {
                if (!title.IsMouseCaptured) return;
                title.ReleaseMouseCapture();
                if (title.IsMouseOver) controller.OpenList(list);
                e.Handled=true;
            };
            title.MouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!title.IsMouseCaptured || e.LeftButton != MouseButtonState.Pressed) return;
                var at=e.GetPosition(title);
                if (Math.Abs(at.X-dragStart.X)<SystemParameters.MinimumHorizontalDragDistance && Math.Abs(at.Y-dragStart.Y)<SystemParameters.MinimumVerticalDragDistance) return;
                title.ReleaseMouseCapture();
                controller.Run(delegate
                {
                    DragMove();
                    RECT bounds;
                    if (!GetWindowRect(Handle, out bounds)) return;
                    list.X = bounds.Left; list.Y = bounds.Top;
                    RestorePosition();
                    if (GetWindowRect(Handle, out bounds)) { list.X = bounds.Left; list.Y = bounds.Top; }
                    library.Save();
                });
            };
            header.Children.Add(title);
            root.Children.Add(header);
            var menu = new ContextMenu { Resources = Resources };
            var rename = new MenuItem { Header = "Переименовать список…" };
            rename.Click += delegate { controller.RenameList(this, list); };
            menu.Items.Add(rename);
            var newList = new MenuItem { Header = "Создать список…" };
            newList.Click += delegate { controller.NewList(this); };
            menu.Items.Add(newList);
            title.ContextMenu = menu;
            root.DragOver += DragOverNote;
            root.Drop += DropNote;

            surface = new ThemedSurface(root);
            Content = surface;
            ApplyTheme(theme);
            Refresh();
            SizeChanged += delegate { if (IsVisible) RestorePosition(); };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e)
            {
                if (disposed) return;
                e.Cancel = true;
                Hide();
            };
        }

        IntPtr Handle { get { return new WindowInteropHelper(this).Handle; } }
        internal void ApplyTheme(ThemeProfile theme) { surface.Apply(this, theme); }

        internal void Refresh()
        {
            int count = library.Index.Notes.Count(n => n.ListId == list.Id);
            title.Text = list.Name + " · " + count;
            Title = list.Name + " · Быстрая заметка";
            Width = 145;
            root.Margin = new Thickness(4);
            title.FontSize = 12;
            title.ToolTip = list.Name + " · " + count + " · перетащить список";
            toggle.ToolTip = "Открыть список";
        }

        string DraggedNote(DragEventArgs e) { return e.Data.GetData(NoteFormat, false) as string; }
        void DragOverNote(object sender, DragEventArgs e)
        {
            string id = DraggedNote(e);
            e.Effects = id != null && library.Index.Notes.Any(n => n.Id == id && n.ListId != list.Id) ? DragDropEffects.Move : DragDropEffects.None;
            e.Handled = true;
        }
        void DropNote(object sender, DragEventArgs e)
        {
            string id = DraggedNote(e);
            if (id != null) controller.Move(id, list);
            e.Handled = true;
        }

        internal void RestorePosition()
        {
            if (!IsVisible) return;
            var screens = System.Windows.Forms.Screen.AllScreens;
            if (screens.Length == 0) return;
            var primary = System.Windows.Forms.Screen.PrimaryScreen ?? screens[0];
            int index = library.Index.Lists.IndexOf(list) % 8;
            int x = list.X ?? (primary.WorkingArea.Left + 25 + index * 32);
            int y = list.Y ?? (primary.WorkingArea.Top + 45 + index * 42);
            var point = new System.Drawing.Point(x, y);
            var screen = screens.FirstOrDefault(s => s.Bounds.Contains(point)) ?? primary;
            RECT bounds;
            if (!GetWindowRect(Handle, out bounds)) return;
            var work = screen.WorkingArea;
            x = Math.Max(work.Left, Math.Min(x, work.Right - (bounds.Right - bounds.Left)));
            y = Math.Max(work.Top, Math.Min(y, work.Bottom - (bounds.Bottom - bounds.Top)));
            SetWindowPos(Handle, IntPtr.Zero, x, y, 0, 0, 0x15);
        }

        public void Dispose() { disposed = true; Close(); }

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr hwnd, out RECT rectangle);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int width, int height, uint flags);
    }
}
