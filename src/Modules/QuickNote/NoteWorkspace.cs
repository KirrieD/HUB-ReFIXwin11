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
            top.MouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e)
            {
                if (commands.IsMouseOver || e.ButtonState != MouseButtonState.Pressed || WindowState != WindowState.Normal) return;
                e.Handled = true;
                DragMove();
            };
            top.Children.Add(heading);
            DockPanel.SetDock(top, Dock.Top);
            root.Children.Add(top);
            tabs.Resources.Add(typeof(TabItem), System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TabItem'><Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabItem'><Border x:Name='tab' CornerRadius='7,7,0,0' Background='Transparent' Padding='5,4' Margin='1,0'><ContentPresenter ContentSource='Header'/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='tab' Property='Background' Value='{DynamicResource Hub.Surface}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>"));
            tabs.Template = (ControlTemplate)System.Windows.Markup.XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='TabControl'><DockPanel><DockPanel DockPanel.Dock='Top' Background='{DynamicResource Hub.Background}'><ScrollViewer HorizontalScrollBarVisibility='Auto' VerticalScrollBarVisibility='Disabled' CanContentScroll='False'><StackPanel Orientation='Horizontal'><TabPanel IsItemsHost='True'/><Button x:Name='NewTabButton' Content='+' ToolTip='Создать или открыть заметку' MinWidth='27' Margin='3'/></StackPanel></ScrollViewer></DockPanel><ContentPresenter x:Name='PART_SelectedContentHost' ContentSource='SelectedContent'/></DockPanel></ControlTemplate>");
            tabs.AddHandler(Button.ClickEvent,new RoutedEventHandler(delegate(object sender,RoutedEventArgs args)
            {
                var button = args.OriginalSource as Button;
                if (button == null || button.Name != "NewTabButton") return;
                var menu = ThemeMenu.Create(Resources);
                menu.PlacementTarget=button;
                foreach (bool markdown in new[] { false,true })
                {
                    var item = new MenuItem { Header=markdown ? "Новая заметка MD" : "Новая заметка" };
                    item.Click += delegate { controller.NewNote(list,markdown); };
                    menu.Items.Add(item);
                }
                var closed=library.Index.Notes.Where(n=>n.ListId==list.Id&&n.TabClosed).ToArray();
                if(closed.Length>0)
                {
                    var reopen=new MenuItem { Header="Открыть закрытую заметку" };
                    foreach(var note in closed)
                    {
                        var item=new MenuItem { Header=note.Name };
                        item.Click+=delegate{controller.Open(note);};
                        reopen.Items.Add(item);
                    }
                    menu.Items.Add(new Separator());
                    menu.Items.Add(reopen);
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
                if(item==null)return;
                if (item.Content == null) controller.Run(() => item.Content = controller.Editor((NoteRecord)item.Tag));
                if (item.Content != null) list.LastNoteId = ((NoteRecord)item.Tag).Id;
                var header = item.Header as FrameworkElement;
                if (header != null) header.BringIntoView();
            };
            Closing += delegate(object sender, System.ComponentModel.CancelEventArgs e) { if (!disposed) { e.Cancel = true; Minimize(); } };
            StateChanged += delegate { if (WindowState == WindowState.Minimized) { WindowState = WindowState.Normal; Minimize(); } };
        }

        internal void ApplyTheme(ThemeProfile theme) { surface.Apply(this, theme); NoteDisclosureStyle.Apply(this, theme); }
        internal void Open(NoteRecord note)
        {
            if(note!=null)library.SetTabClosed(note,false);
            Refresh(note);Show();Activate();
        }
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
            var notes = library.Index.Notes.Where(n => n.ListId == list.Id && !n.TabClosed).ToArray();
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
                    ((DockPanel)item.Header).Children.OfType<TextBlock>().Single().Text = note.Name;
                }
                var target = tabs.Items.OfType<TabItem>().FirstOrDefault(i => i.Tag == wanted) ?? tabs.Items.OfType<TabItem>().FirstOrDefault();
                if (target == null) { list.LastNoteId=null;return; }
                if (target.Content == null) target.Content = controller.Editor((NoteRecord)target.Tag);
                tabs.SelectedItem = target;
                list.LastNoteId = ((NoteRecord)target.Tag).Id;
            }
            finally { refreshing = false; }
        }

        void CloseTab(NoteRecord note)
        {
            controller.Run(delegate
            {
                var item=tabs.Items.OfType<TabItem>().FirstOrDefault(tab=>tab.Tag==note);
                if(item==null)return;
                if(!controller.SaveEditor(note)){tabs.SelectedItem=item;return;}
                var next=tabs.SelectedItem as TabItem;
                if(next==item)
                {
                    int index=tabs.Items.IndexOf(item);
                    next=(index+1<tabs.Items.Count?tabs.Items[index+1]:index>0?tabs.Items[index-1]:null) as TabItem;
                }
                library.SetTabClosed(note,true);
                refreshing=true;
                try { item.Content=null;tabs.Items.Remove(item); }
                finally { refreshing=false; }
                controller.ReleaseEditor(note);
                Refresh(next==null?null:(NoteRecord)next.Tag);
            });
        }

        TabItem Tab(NoteRecord note)
        {
            var header=new DockPanel { Background=Brushes.Transparent };
            var label=new TextBlock { Text=note.Name,MaxWidth=170,TextTrimming=TextTrimming.CharacterEllipsis,Margin=new Thickness(6,3,4,3),VerticalAlignment=VerticalAlignment.Center };
            var close=NoteEditor.Button("×",()=>CloseTab(note));
            close.Width=19;close.Height=19;close.Margin=new Thickness(1,0,2,0);close.Padding=new Thickness(0);close.Focusable=false;
            close.ToolTip="Закрыть заметку";
            System.Windows.Automation.AutomationProperties.SetName(close,"Закрыть заметку");
            DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);header.Children.Add(label);
            var item = new TabItem { Header = header, Tag = note };
            var menu = ThemeMenu.Create(Resources);
            menu.Opened += delegate
            {
                menu.Items.Clear();
                var renameItem=new MenuItem { Header="Переименовать заметку…" };
                renameItem.Click+=delegate
                {
                    menu.IsOpen=false;
                    Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(delegate
                    {
                        if(disposed||note.TabClosed||note.ListId!=list.Id)return;
                        controller.Run(delegate
                        {
                            Open(note);
                            UpdateLayout();
                            controller.Editor(note).Rename();
                        });
                    }));
                };
                menu.Items.Add(renameItem);
                var closeItem=new MenuItem { Header="Закрыть заметку" };
                closeItem.Click+=delegate{CloseTab(note);};
                menu.Items.Add(closeItem);
                var targets=library.Index.Lists.Where(l=>l.Id!=list.Id).ToArray();
                if(targets.Length>0)menu.Items.Add(new Separator());
                foreach (var target in targets)
                {
                    var move = new MenuItem { Header = "Перенести → " + target.Name };
                    move.Click += delegate { controller.Move(note.Id, target); };
                    menu.Items.Add(move);
                }
            };
            header.ContextMenu = menu;
            Point start = new Point();
            bool armed = false;
            header.PreviewMouseLeftButtonDown += delegate(object sender, MouseButtonEventArgs e) { start = e.GetPosition(header); armed = !close.IsMouseOver; };
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
