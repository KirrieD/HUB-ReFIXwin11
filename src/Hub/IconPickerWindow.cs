using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ReFix.Shared;

namespace ReFix.Hub
{
    internal sealed class IconPickerWindow : Window
    {
        sealed class Target
        {
            internal readonly string Id, Name;
            internal Target(string id,string name) { Id=id; Name=name; }
            public override string ToString() { return Name; }
        }

        readonly ThemeStore themes;
        readonly ComboBox target=new ComboBox { Margin=new Thickness(0,5,0,12) };
        readonly TextBox search=new TextBox { Margin=new Thickness(0,5,0,12) };
        readonly TextBlock notice=Ui.Text("",12,"Hub.Muted");
        readonly HubIcon preview=new HubIcon(26);
        readonly Dictionary<WindowsStockIcon,Button> tiles=new Dictionary<WindowsStockIcon,Button>();
        readonly Border frame=new Border { Margin=new Thickness(14),BorderThickness=new Thickness(1) };
        readonly Border background=new Border { IsHitTestVisible=false };

        internal IconPickerWindow(ThemeStore themes,IEnumerable<ModuleView> modules)
        {
            this.themes=themes;
            Title="HUB ReFIX — значки Windows";
            Width=565; Height=660; MinWidth=470; MinHeight=430;
            WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=Brushes.Transparent;
            ResizeMode=ResizeMode.CanResizeWithGrip; ShowInTaskbar=false;
            Ui.Styles(this);
            Content=frame;
            var layers=new Grid();frame.Child=layers;layers.Children.Add(background);
            var root=new DockPanel { Margin=new Thickness(20) };layers.Children.Add(root);
            var header=new DockPanel { Background=Brushes.Transparent,Margin=new Thickness(0,0,0,15) };
            var close=Ui.Button("✕",Close,false);close.ToolTip="Закрыть";
            DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
            header.Children.Add(Ui.Text("Значки Windows",24,"Hub.Foreground"));
            header.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e)
            {
                if(!close.IsMouseOver&&e.ButtonState==MouseButtonState.Pressed)DragMove();
            };
            DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);

            var filters=new StackPanel();
            filters.Children.Add(Ui.Text("Приложение или расширение",12,"Hub.Foreground"));
            System.Windows.Automation.AutomationProperties.SetName(target,"Приложение или расширение");
            filters.Children.Add(target);
            filters.Children.Add(Ui.Text("Поиск значка",12,"Hub.Foreground"));
            System.Windows.Automation.AutomationProperties.SetName(search,"Поиск значка");
            filters.Children.Add(search);
            DockPanel.SetDock(filters,Dock.Top);root.Children.Add(filters);

            var footer=new DockPanel { Margin=new Thickness(0,14,0,0) };
            var restore=Ui.Button("По умолчанию",()=>SelectIcon(null),false);
            DockPanel.SetDock(restore,Dock.Right);footer.Children.Add(restore);
            preview.Margin=new Thickness(0,0,8,0);DockPanel.SetDock(preview,Dock.Left);footer.Children.Add(preview);
            notice.VerticalAlignment=VerticalAlignment.Center;footer.Children.Add(notice);
            DockPanel.SetDock(footer,Dock.Bottom);root.Children.Add(footer);

            var gallery=new UniformGrid { Columns=4 };
            foreach(var option in WindowsStockIcons.Options)
            {
                using(var bitmap=WindowsStockIcons.CreateBitmap(option.Id,32))
                {
                    if(bitmap==null)continue;
                    var button=Ui.Button("",()=>SelectIcon(option.Id),false);
                    button.Margin=new Thickness(3);
                    button.Padding=new Thickness(4,6,4,6);
                    button.HorizontalContentAlignment=HorizontalAlignment.Stretch;
                    var label=Ui.Text(option.Name,11,"Hub.Foreground");label.TextAlignment=TextAlignment.Center;
                    var tile=new StackPanel();
                    tile.Children.Add(new Image { Source=HubIcon.ToSource(bitmap),Width=28,Height=28,Margin=new Thickness(0,0,0,5) });
                    tile.Children.Add(label);
                    button.Content=tile;
                    System.Windows.Automation.AutomationProperties.SetName(button,option.Name);
                    gallery.Children.Add(button);
                    tiles.Add(option,button);
                }
            }
            var items=new StackPanel();
            var empty=Ui.Text("Значки не найдены",13,"Hub.Muted");empty.Margin=new Thickness(4,8,4,0);
            items.Children.Add(empty);items.Children.Add(gallery);
            root.Children.Add(new ScrollViewer { Content=items,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled });

            search.TextChanged+=delegate
            {
                foreach(var tile in tiles)
                    tile.Value.Visibility=tile.Key.Name.IndexOf(search.Text.Trim(),StringComparison.CurrentCultureIgnoreCase)>=0?Visibility.Visible:Visibility.Collapsed;
                empty.Visibility=tiles.Values.Any(button=>button.Visibility==Visibility.Visible)?Visibility.Collapsed:Visibility.Visible;
            };
            empty.Visibility=tiles.Count==0?Visibility.Visible:Visibility.Collapsed;
            target.SelectionChanged+=delegate{ Refresh(); };
            UpdateTargets(modules);
            themes.Changed+=ThemeChanged;
            Closed+=delegate{themes.Changed-=ThemeChanged;};
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(e.Key==Key.Escape){e.Handled=true;Close();}};
            ApplyTheme();
        }

        internal void UpdateTargets(IEnumerable<ModuleView> modules)
        {
            var previous=target.SelectedItem as Target;
            target.Items.Clear();
            target.Items.Add(new Target("hub","HUB ReFIX"));
            foreach(var module in modules.OrderBy(m=>m.Manifest.Name,StringComparer.CurrentCultureIgnoreCase))
                if(module.Manifest.Id!="hub")target.Items.Add(new Target(module.Manifest.Id,module.Manifest.Name));
            target.SelectedItem=previous==null?target.Items[0]:target.Items.OfType<Target>().FirstOrDefault(item=>item.Id==previous.Id)??target.Items[0];
        }

        void SelectIcon(int? icon)
        {
            var current=target.SelectedItem as Target;
            if(current==null)return;
            try { themes.SetWindowsIcon(current.Id,icon);Refresh(); }
            catch(Exception ex){notice.Text=ex.Message;}
        }

        void Refresh()
        {
            var current=target.SelectedItem as Target;
            if(current==null)return;
            var theme=themes.Current;
            var id=theme.WindowsIconFor(current.Id);
            var option=WindowsStockIcons.Options.FirstOrDefault(icon=>icon.Id==id);
            preview.Update(current.Id,theme);
            notice.Text=themes.LastError??(option==null?"Значок по умолчанию":option.Name);
            foreach(var tile in tiles)
            {
                bool selected=tile.Key.Id==id;
                tile.Value.SetResourceReference(Control.BackgroundProperty,selected?"Hub.NoticeBackground":"Hub.Surface");
                tile.Value.SetResourceReference(Control.BorderBrushProperty,selected?"Hub.Accent":"Hub.Border");
            }
        }

        void ThemeChanged(object sender,EventArgs e)
        {
            if(!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{if(IsLoaded){ApplyTheme();Refresh();}}));
        }
        void ApplyTheme() { Ui.Apply(this,themes.Current,frame,background,true); }
    }
}
