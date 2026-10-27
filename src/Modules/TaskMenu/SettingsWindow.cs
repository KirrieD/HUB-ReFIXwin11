using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using ReFix.Shared;
using ReFix.Modules;

namespace ReFix.TaskMenu
{
    internal sealed class SettingsWindow : Window
    {
        readonly TaskMenuConfig config;readonly Topology topology;readonly Action save;readonly ListBox panels=new ListBox{MinHeight=150};readonly ComboBox monitors=new ComboBox(),edges=new ComboBox();readonly TextBox columns=new TextBox{Text="4"},size=new TextBox{Text="40"};readonly TextBlock status=new TextBlock{TextWrapping=TextWrapping.Wrap};readonly ThemedSurface surface;bool loading;
        internal SettingsWindow(TaskMenuConfig config,Topology topology,ThemeProfile theme,Action save)
        {
            this.config=config;this.topology=topology;this.save=save;Title="Меню задач v"+ProductInfo.Version+" · настройки";Width=570;Height=610;MinWidth=480;MinHeight=540;WindowStartupLocation=WindowStartupLocation.CenterScreen;ThemeApplier.Apply(this,theme);SetResourceReference(Window.BackgroundProperty,"Hub.Background");SetResourceReference(Window.ForegroundProperty,"Hub.Foreground");
            WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.CanResizeWithGrip;
            Height=Math.Min(720,SystemParameters.WorkArea.Height);MinHeight=Math.Min(MinHeight,Height);ThemeControls.Install(this);
            var root=new DockPanel{Margin=new Thickness(22)};var heading=new StackPanel();var title=new DockPanel();var close=Button("✕",Close);DockPanel.SetDock(close,Dock.Right);title.Children.Add(close);title.Children.Add(new TextBlock{Text="Меню задач · v"+ProductInfo.Version,FontSize=24});title.MouseLeftButtonDown+=delegate{try{DragMove();}catch(InvalidOperationException){}};heading.Children.Add(title);heading.Children.Add(new TextBlock{Text="До трёх независимых панелей на каждом экране.\nСторона панели задач Windows недоступна.",Margin=new Thickness(0,8,0,16),TextWrapping=TextWrapping.Wrap});DockPanel.SetDock(heading,Dock.Top);root.Children.Add(heading);
            var bottom=new StackPanel();bottom.Children.Add(new TextBlock{Text="Наведите на цветную полосу у края экрана. Добавляйте файлы и папки перетаскиванием или через правую кнопку мыши. Порядок элементов меняется в их контекстном меню.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,8)});bottom.Children.Add(status);DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
            var body=new StackPanel();body.Children.Add(panels);var commands=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,8,0,14)};commands.Children.Add(Button("Новая панель",NewPanel));commands.Children.Add(Button("Удалить",Delete));body.Children.Add(commands);
            AddField(body,"Экран",monitors);AddField(body,"Сторона",edges);AddField(body,"Столбцов (1–12)",columns);AddField(body,"Размер значков (24–96)",size);body.Children.Add(Button("Применить к панели",Apply));root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});surface=new ThemedSurface(root);Content=surface;surface.Apply(this,theme);
            foreach(var monitor in topology.Monitors)monitors.Items.Add(monitor);monitors.SelectionChanged+=delegate{if(!loading)RefreshEdges();};panels.SelectionChanged+=delegate{LoadSelection();};Reload();
        }
        static Button Button(string title,Action action){var b=new Button{Content=title,Margin=new Thickness(0,0,8,0),Padding=new Thickness(12,6,12,6)};b.Click+=delegate{action();};return b;}
        static void AddField(Panel body,string title,Control control){var row=new Grid{Margin=new Thickness(0,0,0,8)};row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(190)});row.ColumnDefinitions.Add(new ColumnDefinition());row.Children.Add(new TextBlock{Text=title,VerticalAlignment=VerticalAlignment.Center});Grid.SetColumn(control,1);row.Children.Add(control);body.Children.Add(row);}
        void Reload(){string selected=panels.SelectedItem is TaskMenuPanel?((TaskMenuPanel)panels.SelectedItem).Id:null;panels.Items.Clear();foreach(var p in config.Panels){panels.Items.Add(p);if(p.Id==selected)panels.SelectedItem=p;}if(panels.SelectedIndex<0&&panels.Items.Count>0)panels.SelectedIndex=0;if(panels.Items.Count==0&&monitors.Items.Count>0){monitors.SelectedIndex=0;RefreshEdges();}status.Text="Панелей: "+config.Panels.Count;}
        void LoadSelection(){var p=panels.SelectedItem as TaskMenuPanel;if(p==null)return;loading=true;monitors.SelectedIndex=-1;foreach(MonitorInfo m in monitors.Items)if(String.Equals(m.Id,p.Monitor,StringComparison.OrdinalIgnoreCase)){monitors.SelectedItem=m;break;}columns.Text=p.Columns.ToString();size.Text=p.IconSize.ToString();loading=false;RefreshEdges();if(edges.Items.Contains(p.Edge))edges.SelectedItem=p.Edge;else status.Text="Панель приостановлена: экран отключён или эта сторона занята панелью задач. Выберите доступное размещение.";}
        void RefreshEdges(){object selected=edges.SelectedItem;edges.Items.Clear();var m=monitors.SelectedItem as MonitorInfo;if(m==null)return;foreach(TaskMenuEdge e in Enum.GetValues(typeof(TaskMenuEdge)))if(!m.Forbidden.Contains(e))edges.Items.Add(e);if(selected!=null&&edges.Items.Contains(selected))edges.SelectedItem=selected;else if(edges.Items.Count>0)edges.SelectedIndex=0;}
        bool Placement(TaskMenuPanel current,out MonitorInfo monitor,out TaskMenuEdge edge){monitor=monitors.SelectedItem as MonitorInfo;edge=edges.SelectedItem is TaskMenuEdge?(TaskMenuEdge)edges.SelectedItem:TaskMenuEdge.Left;if(monitor==null||edges.SelectedItem==null){status.Text="Выберите подключённый экран и доступную сторону.";return false;}int count=0;foreach(var p in config.Panels){if(p==current||!String.Equals(p.Monitor,monitor.Id,StringComparison.OrdinalIgnoreCase))continue;count++;if(p.Edge==edge){status.Text="На этой стороне уже есть панель.";return false;}}if(count>=3){status.Text="На одном экране может быть не более трёх панелей.";return false;}return true;}
        void NewPanel(){MonitorInfo m;TaskMenuEdge edge;if(!Placement(null,out m,out edge))return;var panel=new TaskMenuPanel{Monitor=m.Id,Edge=edge,Columns=edge==TaskMenuEdge.Left||edge==TaskMenuEdge.Right?1:4};config.Panels.Add(panel);save();Reload();panels.SelectedItem=panel;}
        void Delete(){var p=panels.SelectedItem as TaskMenuPanel;if(p==null)return;config.Panels.Remove(p);save();Reload();}
        void Apply(){var p=panels.SelectedItem as TaskMenuPanel;if(p==null){status.Text="Сначала создайте панель.";return;}MonitorInfo m;TaskMenuEdge edge;if(!Placement(p,out m,out edge))return;int c,s;if(!Int32.TryParse(columns.Text,out c)||c<1||c>12||!Int32.TryParse(size.Text,out s)||s<24||s>96){status.Text="Столбцов: 1–12; размер значков: 24–96.";return;}p.Monitor=m.Id;p.Edge=edge;p.Columns=c;p.IconSize=s;save();Reload();}
        internal void ApplyTheme(ThemeProfile theme){surface.Apply(this,theme);}
    }
}
