using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ReFix.Modules;
using ReFix.Shared;

namespace ReFix.QuickNote
{
    internal sealed class NoteWorkspace : Window, IDisposable
    {
        internal const string NoteFormat = "ReFIX.QuickNote.Note";
        readonly NoteController controller;
        readonly Library library;
        readonly NoteList list;
        readonly TabControl tabs = new TabControl();
        readonly ThemedSurface surface;
        readonly TextBlock heading;
        bool refreshing, disposed;

        internal NoteWorkspace(NoteController controller, Library library, NoteList list, ThemeProfile theme)
        {
            this.controller = controller; this.library = library; this.list = list;
            Width = 950; Height = 660; MinWidth = 480; MinHeight = 390;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None; AllowsTransparency = true;
            Background = Brushes.Transparent; ResizeMode = ResizeMode.CanResizeWithGrip;
            ThemeControls.Install(this, true);
            NoteDisclosureStyle.Install(this);
            var root = new DockPanel { Margin = new Thickness(8) };
            var top = new DockPanel { Background = Brushes.Transparent };
            var commands = new StackPanel { Orientation = Orientation.Horizontal };
            commands.Children.Add(NoteEditor.Button("−", Minimize));
            commands.Children.Add(NoteEditor.Button("□", delegate { WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized; }));
            commands.Children.Add(NoteEditor.Button("×", Minimize));
            DockPanel.SetDock(commands, Dock.Right);
            top.Children.Add(commands);
            heading = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8,0,0,0) };
            heading.MouseLeftButtonDown += delegate { if (Mouse.LeftButton == MouseButtonState.Pressed && WindowState == WindowState.Normal) DragMove(); };
            top.Children.Add(heading);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
            tabs.Resources.Add(typeof(TabItem), System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TabItem'><Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabItem'><Border x:Name='tab' CornerRadius='7,7,0,0' Background='Transparent' Padding='5,4' Margin='1,0'><ContentPresenter ContentSource='Header'/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='tab' Property='Background' Value='{DynamicResource Hub.Surface}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>"));
            tabs.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TabControl'><DockPanel><DockPanel DockPanel.Dock='Top' Background='{DynamicResource Hub.Background}'><ScrollViewer HorizontalScrollBarVisibility='Auto' VerticalScrollBarVisibility='Disabled' CanContentScroll='False'><StackPanel Orientation='Horizontal'><TabPanel IsItemsHost='True'/><Button x:Name='NewTabButton' Content='+' ToolTip='Добавить заметку' MinWidth='27' Margin='3'/></StackPanel></ScrollViewer></DockPanel><ContentPresenter x:Name='PART_SelectedContentHost' ContentSource='SelectedContent'/></DockPanel></ControlTemplate>");
            tabs.AddHandler(Button.ClickEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs args)
            {
                var button = args.OriginalSource as Button;
                if (button == null || button.Name != "NewTabButton") return;
                var menu = new ContextMenu { PlacementTarget=button, Resources=Resources };
                foreach (bool markdown in new[] { false,true })
                {
                    var item = new MenuItem { Header=markdown ? "Новая заметка MD" : "Новая заметка" };
                    item.Click += delegate { controller.NewNote(list,markdown); };
                    menu.Items.Add(item);
                }
                menu.IsOpen=true;
            }));
            root.Children.Add(tabs);
            surface = new ThemedSurface(root);
            Content = surface;
            ApplyTheme(theme);
            tabs.SelectionChanged += delegate(object sender, SelectionChangedEventArgs e)
            {
                if (refreshing || e.Source != tabs) return;
                var item = tabs.SelectedItem as TabItem;
                if (item != null && item.Content == null) controller.Run(() => item.Content = controller.Editor((NoteRecord)item.Tag));
                if (item.Content != null) list.LastNoteId = ((NoteRecord)item.Tag).Id;
                var header = item.Header as FrameworkElement;
                if (header != null) header.BringIntoView();
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!disposed) { e.Cancel = true; Minimize(); } };
            StateChanged += delegate { if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; Minimize(); } };
        }

        internal void ApplyTheme(ThemeProfile theme) { surface.Apply(this, theme); NoteDisclosureStyle.Apply(this, theme); }
        internal void Open(NoteRecord note) { Refresh(note); Show(); Activate(); }
        internal void Minimize() { if (controller.SaveList(list.Id)) Hide(); }

        internal void Detach(string id)
        {
            var item = tabs.Items.OfType<TabItem>().FirstOrDefault(i => ((NoteRecord)i.Tag).Id == id);
            if (item != null) item.Content = null;
        }

        internal void Refresh(NoteRecord preferred = null)
        {
            heading.Text = list.Name;
            Title = list.Name + " · Быстрая заметка";
            var notes = library.Index.Notes.Where(n => n.ListId == list.Id).ToArray();
            var items = tabs.Items.OfType<TabItem>().ToArray();
            var selected = tabs.SelectedItem as TabItem;
            var wanted = preferred ?? (selected == null ? notes.FirstOrDefault(n => n.Id == list.LastNoteId) : (NoteRecord)selected.Tag);
            refreshing = true;
            try
            {
                if (!items.Select(i => i.Tag).SequenceEqual(notes.Cast<object>()))
                {
                    foreach (var item in items) item.Content = null;
                    tabs.Items.Clear();
                    foreach (var note in notes) tabs.Items.Add(Tab(note));
                }
                foreach (TabItem item in tabs.Items)
                {
                    var note = (NoteRecord)item.Tag;
                    ((TextBlock)item.Header).Text = note.Name;
                }
                var target = tabs.Items.OfType<TabItem>().FirstOrDefault(i => i.Tag == wanted) ?? tabs.Items.OfType<TabItem>().FirstOrDefault();
                if (target == null) return;
                if (target.Content == null) target.Content = controller.Editor((NoteRecord)target.Tag);
                tabs.SelectedItem = target;
                list.LastNoteId = ((NoteRecord)target.Tag).Id;
            }
            finally { refreshing = false; }
        }

        TabItem Tab(NoteRecord note)
        {
            var header = new TextBlock { Text = note.Name, MaxWidth = 170, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(6,3,6,3) };
            var item = new TabItem { Header = header, Tag = note };
            var menu = new ContextMenu { Resources = Resources };
            menu.Opened += delegate
            {
                menu.Items.Clear();
                foreach (var target in library.Index.Lists.Where(l => l.Id != list.Id))
                {
                    var move = new MenuItem { Header = "Перенести → " + target.Name };
                    move.Click += delegate { controller.Move(note.Id, target); };
                    menu.Items.Add(move);
                }
                if (menu.Items.Count == 0) menu.Items.Add(new MenuItem { Header = "Других списков пока нет", IsEnabled = false });
            };
            header.ContextMenu = menu;
            Point start = new Point();
            bool armed = false;
            header.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { start = e.GetPosition(header); armed = true; };
            header.PreviewMouseLeftButtonUp += delegate { armed = false; };
            header.PreviewMouseMove += delegate(object sender, MouseEventArgs e)
            {
                if (!armed || e.LeftButton != MouseButtonState.Pressed || !item.IsSelected) return;
                var now = e.GetPosition(header);
                if (Math.Abs(start.X - now.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(start.Y - now.Y) < SystemParameters.MinimumVerticalDragDistance) return;
                armed = false;
                DragDrop.DoDragDrop(header, new DataObject(NoteFormat, note.Id), DragDropEffects.Move);
                start = e.GetPosition(header);
            };
            item.AllowDrop = true;
            item.DragOver += delegate(object sender, DragEventArgs e) { e.Effects = e.Data.GetDataPresent(NoteFormat, false) ? DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
            item.Drop += delegate(object sender, DragEventArgs e) { controller.Move(e.Data.GetData(NoteFormat, false) as string, list); e.Handled = true; };
            return item;
        }

        public void Dispose() { disposed = true; Close(); }
    }
}
