using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using ReFix.Shared;
using ReFix.Modules;
using System.Windows.Controls;
using System.Windows.Media;

namespace ReFix.QuickNote
{
    internal sealed class NoteController : IDisposable
    {
        readonly Library library;
        readonly ThemeStore themes;
        readonly ControlsStore controls;
        readonly Dictionary<string, NoteEditor> editors = new Dictionary<string, NoteEditor>();
        readonly Dictionary<string, NoteWorkspace> workspaces = new Dictionary<string, NoteWorkspace>();
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
            if (!workspaces.Values.Any(w => w.IsVisible) && !lists.Values.Any(w => w.IsVisible)) { Show(); return; }
            if (!SaveAll()) return;
            foreach (var workspace in workspaces.Values) workspace.Hide();
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

        internal void NewNote(NoteList list, bool markdown = false) { Run(delegate { var note = library.AddNote(list, markdown); RefreshLists(); Open(note); }); }

        internal NoteEditor Editor(NoteRecord note)
        {
            NoteEditor editor;
            if (!editors.TryGetValue(note.Id, out editor))
            {
                editor = new NoteEditor(library, note, controls, Hidden, Updated);
                editor.CreateNote = markdown => NewNote(library.Index.Lists.First(l => l.Id == note.ListId), markdown);
                editors.Add(note.Id, editor);
            }
            return editor;
        }

        internal void Open(NoteRecord note) { OpenList(library.Index.Lists.First(l => l.Id == note.ListId), note); }

        internal void OpenList(NoteList list, NoteRecord note = null)
        {
            Run(delegate
            {
                NoteWorkspace workspace;
                if (!workspaces.TryGetValue(list.Id, out workspace))
                {
                    workspace = new NoteWorkspace(this, library, list, themes.Current);
                    workspaces.Add(list.Id, workspace);
                }
                workspace.Open(note);
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
            Run(delegate
            {
                NoteEditor editor;
                if (editors.TryGetValue(note.Id, out editor) && !editor.Save()) return;
                NoteWorkspace previous;
                if (workspaces.TryGetValue(note.ListId, out previous)) previous.Detach(note.Id);
                try { library.Move(note, target); }
                finally { RefreshLists(); }
            });
        }

        void Hidden(NoteRecord note)
        {
            var list = library.Index.Lists.First(l => l.Id == note.ListId);
            NoteWorkspace workspace;
            if (workspaces.TryGetValue(list.Id, out workspace)) workspace.Minimize();
            ShowList(list);
        }

        void Updated(NoteRecord note) { RefreshLists(); }
        void RefreshLists() { foreach (var window in lists.Values) window.Refresh(); foreach (var window in workspaces.Values) window.Refresh(); }

        internal bool SaveAll()
        {
            bool ok = true;
            foreach (var editor in editors.Values)
                if (!editor.Save()) ok = false;
            return ok && (workspaces.Count == 0 || SaveSelection());
        }

        internal bool SaveList(string id)
        {
            bool ok = true;
            foreach (var note in library.Index.Notes.Where(n => n.ListId == id))
            {
                NoteEditor editor;
                if (editors.TryGetValue(note.Id, out editor) && !editor.Save()) ok = false;
            }
            return ok && SaveSelection();
        }

        bool SaveSelection()
        {
            try { library.Save(); return true; }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Быстрая заметка", MessageBoxButton.OK, MessageBoxImage.Warning); return false; }
        }

        internal void Run(Action action)
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Быстрая заметка", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }

        string Prompt(Window owner, string title, string value)
        {
            var field = new System.Windows.Controls.TextBox { Text = value, MaxLength = 80, Margin = new Thickness(0, 0, 0, 8) };
            var panel = new System.Windows.Controls.StackPanel { Margin = new Thickness(16) };
            panel.Children.Add(field);
            var dialog = new Window { Owner = owner, Title = title, Width = 330, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent, ShowInTaskbar = false };
            ThemeControls.Install(dialog);
            field.SetResourceReference(Control.BackgroundProperty,"Hub.Surface");
            field.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");
            field.SetResourceReference(Control.BorderBrushProperty,"Hub.Border");
            field.Padding = new Thickness(6);
            var heading = new DockPanel { Margin = new Thickness(0, 0, 0, 10) };
            var close = NoteEditor.Button("×", dialog.Close);
            close.IsCancel = true;
            DockPanel.SetDock(close, Dock.Right);
            heading.Children.Add(close);
            heading.Children.Add(new TextBlock { Text = title, FontSize = 17, VerticalAlignment = VerticalAlignment.Center });
            panel.Children.Insert(0, heading);
            var surface = new ThemedSurface(panel);
            dialog.Content = surface;
            surface.Apply(dialog, themes.Current);
            var ok = NoteEditor.Button("Сохранить", delegate { if (!String.IsNullOrWhiteSpace(field.Text)) dialog.DialogResult = true; });
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
                foreach (var window in workspaces.Values) window.ApplyTheme(profile);
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
            foreach (var window in workspaces.Values) window.Dispose();
            foreach (var editor in editors.Values) editor.Dispose();
            foreach (var window in lists.Values) window.Dispose();
        }
    }
}
