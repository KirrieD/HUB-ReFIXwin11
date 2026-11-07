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
        const string NoteFormat = "ReFIX.QuickNote.Note";
        readonly NoteList list;
        readonly Library library;
        readonly NoteController controller;
        readonly ThemedSurface surface;
        readonly StackPanel notes = new StackPanel();
        readonly StackPanel content = new StackPanel();
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
            toggle = NoteWindow.Button("", Toggle);
            toggle.Margin = new Thickness(0);
            toggle.Padding = new Thickness(5, 0, 5, 0);
            toggle.MinWidth = 20;
            DockPanel.SetDock(toggle, Dock.Right);
            header.Children.Add(toggle);
            title = new TextBlock { FontSize = 15, Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Cursor = Cursors.SizeAll };
            title.ToolTip = "Перетащить список";
            title.MouseLeftButtonDown += delegate
            {
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
            root.Children.Add(content);

            content.Children.Add(new ScrollViewer { Content = notes, MaxHeight = 275, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            var commands = new WrapPanel { Margin = new Thickness(0, 5, 0, 0) };
            commands.Children.Add(NoteWindow.Button("+ Заметка", () => controller.NewNote(list)));
            commands.Children.Add(NoteWindow.Button("+ Список", () => controller.NewList(this)));
            content.Children.Add(commands);

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
                if (!list.Collapsed) Toggle();
            };
        }

        IntPtr Handle { get { return new WindowInteropHelper(this).Handle; } }
        internal void ApplyTheme(ThemeProfile theme) { surface.Apply(this, theme); }

        internal void Refresh()
        {
            title.Text = list.Name;
            Title = list.Name + " · Быстрая заметка";
            toggle.Content = list.Collapsed ? "›" : "⌄";
            toggle.ToolTip = list.Collapsed ? "Раскрыть заметки" : "Свернуть список";
            Width = list.Collapsed ? 119 : 280;
            root.Margin = new Thickness(list.Collapsed ? 4 : 10);
            title.FontSize = list.Collapsed ? 12 : 15;
            title.ToolTip = list.Name + " · перетащить список";
            content.Visibility = list.Collapsed ? Visibility.Collapsed : Visibility.Visible;
            notes.Children.Clear();
            foreach (var note in library.Index.Notes.Where(n => n.ListId == list.Id))
            {
                var row = NoteWindow.Button("▤  " + note.Name, () => controller.Open(note));
                row.HorizontalContentAlignment = HorizontalAlignment.Left;
                row.ToolTip = note.Name;
                row.ContextMenu = MoveMenu(note);
                Point start = new Point();
                row.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { start = e.GetPosition(row); };
                row.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
                {
                    if (e.LeftButton != MouseButtonState.Pressed || !row.IsMouseCaptured) return;
                    var at = e.GetPosition(row);
                    if (Math.Abs(at.X - start.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(at.Y - start.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                    row.ReleaseMouseCapture();
                    e.Handled = true;
                    DragDrop.DoDragDrop(row, new DataObject(NoteFormat, note.Id), DragDropEffects.Move);
                };
                notes.Children.Add(row);
            }
            if (notes.Children.Count == 0) notes.Children.Add(new TextBlock { Text = "Нет заметок", Margin = new Thickness(6, 8, 0, 8), Opacity = .7 });
        }

        ContextMenu MoveMenu(NoteRecord note)
        {
            var menu = new ContextMenu { Resources = Resources };
            var move = new MenuItem { Header = "Перенести в список" };
            foreach (var target in library.Index.Lists.Where(l => l.Id != list.Id))
            {
                var item = new MenuItem { Header = target.Name };
                item.Click += delegate { controller.Move(note.Id, target); };
                move.Items.Add(item);
            }
            move.IsEnabled = move.Items.Count > 0;
            menu.Items.Add(move);
            return menu;
        }

        void Toggle()
        {
            controller.Run(delegate
            {
                list.Collapsed = !list.Collapsed;
                try { library.Save(); }
                catch { list.Collapsed = !list.Collapsed; throw; }
                Refresh();
                UpdateLayout();
                RestorePosition();
            });
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
