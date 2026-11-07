using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using ReFix.Shared;

namespace ReFix.QuickNote
{
    internal sealed class NoteController : IDisposable
    {
        readonly Library library;
        readonly ThemeStore themes;
        readonly ControlsStore controls;
        readonly Dictionary<string, NoteWindow> editors = new Dictionary<string, NoteWindow>();
        readonly Dictionary<string, ListWindow> lists = new Dictionary<string, ListWindow>();
        readonly System.Windows.Threading.Dispatcher dispatcher = System.Windows.Threading.Dispatcher.CurrentDispatcher;
        bool disposed;

        internal NoteController(Library library, ThemeStore themes, ControlsStore controls)
        {
            this.library = library;
            this.themes = themes;
            this.controls = controls;
            themes.Changed += ThemeChanged;
            SystemEvents.DisplaySettingsChanged += DisplayChanged;
        }

        internal void Show() { foreach (var list in library.Index.Lists) ShowList(list); }

        internal void Toggle()
        {
            if (!editors.Values.Any(w => w.IsVisible) && !lists.Values.Any(w => w.IsVisible)) { Show(); return; }
            if (!SaveAll()) return;
            foreach (var editor in editors.Values) editor.Hide();
            foreach (var window in lists.Values) window.Hide();
        }

        void ShowList(NoteList list)
        {
            ListWindow window;
            if (!lists.TryGetValue(list.Id, out window))
            {
                window = new ListWindow(list, library, this, themes.Current);
                lists.Add(list.Id, window);
            }
            if (!window.IsVisible)
            {
                window.Show();
                window.RestorePosition();
            }
        }

        internal void NewNote(NoteList list) { Run(() => Open(library.AddNote(list))); }

        internal void Open(NoteRecord note)
        {
            Run(delegate
            {
                NoteWindow editor;
                if (!editors.TryGetValue(note.Id, out editor))
                {
                    editor = new NoteWindow(library, note, themes.Current, controls, Hidden, Updated);
                    editors.Add(note.Id, editor);
                }
                editor.Show();
                editor.Activate();
            });
        }

        internal void NewList(ListWindow owner)
        {
            string name = Prompt(owner, "Новый список", "");
            if (name == null) return;
            Run(delegate { ShowList(library.AddList(name)); RefreshLists(); });
        }

        internal void RenameList(ListWindow owner, NoteList list)
        {
            string name = Prompt(owner, "Название списка", list.Name);
            if (name == null) return;
            Run(delegate
            {
                string previous = list.Name;
                list.Name = name;
                try { library.Save(); }
                catch { list.Name = previous; throw; }
                RefreshLists();
            });
        }

        internal void Move(string noteId, NoteList target)
        {
            var note = library.Index.Notes.FirstOrDefault(n => n.Id == noteId);
            if (note == null || note.ListId == target.Id) return;
            Run(delegate { library.Move(note, target); RefreshLists(); });
        }

        void Hidden(NoteRecord note)
        {
            var list = library.Index.Lists.First(l => l.Id == note.ListId);
            ShowList(list);
            Updated(note);
        }

        void Updated(NoteRecord note) { RefreshLists(); }
        void RefreshLists() { foreach (var window in lists.Values) window.Refresh(); }

        internal bool SaveAll()
        {
            bool ok = true;
            foreach (var editor in editors.Values)
                if (!editor.Save()) { editor.Show(); ok = false; }
            return ok;
        }

        internal void Run(Action action)
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Быстрая заметка", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        static string Prompt(Window owner, string title, string value)
        {
            var field = new System.Windows.Controls.TextBox { Text = value, MaxLength = 80, Margin = new Thickness(0, 0, 0, 8) };
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(field);
            var dialog = new Window { Owner = owner, Title = title, Width = 330, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Content = panel };
            var ok = NoteWindow.Button("Сохранить", delegate { if (!String.IsNullOrWhiteSpace(field.Text)) dialog.DialogResult = true; });
            ok.IsDefault = true;
            panel.Children.Add(ok);
            dialog.Loaded += delegate { field.Focus(); field.SelectAll(); };
            return dialog.ShowDialog() == true ? field.Text.Trim() : null;
        }

        void ThemeChanged(object sender, EventArgs e)
        {
            if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(new Action(delegate
            {
                if (disposed) return;
                var profile = themes.Current;
                foreach (var window in editors.Values) window.ApplyTheme(profile);
                foreach (var window in lists.Values) window.ApplyTheme(profile);
            }));
        }

        void DisplayChanged(object sender, EventArgs e)
        {
            if (!dispatcher.HasShutdownStarted) dispatcher.BeginInvoke(new Action(delegate
            {
                if (!disposed) foreach (var window in lists.Values) if (window.IsVisible) window.RestorePosition();
            }));
        }

        public void Dispose()
        {
            disposed = true;
            themes.Changed -= ThemeChanged;
            SystemEvents.DisplaySettingsChanged -= DisplayChanged;
            foreach (var editor in editors.Values) editor.Dispose();
            foreach (var window in lists.Values) window.Dispose();
        }
    }
}
