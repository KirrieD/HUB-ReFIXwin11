using System;
using System.Windows;
using System.Windows.Controls;
using ReFix.Shared;
using ReFix.Modules;
using ReFix.TaskMenu;
namespace ReFix.MiniIcons
{
    internal sealed class SettingsWindow:Window
    {
        readonly MiniIconsConfig config;readonly Topology topology;readonly Action save;readonly ThemedSurface surface;readonly ComboBox monitors=new ComboBox();readonly TextBox size=new TextBox(),columns=new TextBox();readonly CheckBox windows=new CheckBox{Content="Показывать отдельный раздел сторонних значков Windows"};readonly TextBlock status=Visuals.Text("",12,"Hub.Muted");
        internal SettingsWindow(MiniIconsConfig config,Topology topology,ThemeProfile theme,Action save,Action openHub)
        {
            this.config=config;this.topology=topology;this.save=save;Title="Мини значки v"+ProductInfo.Version+" · настройки";Width=560;Height=590;MinWidth=440;MinHeight=490;WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.CanResizeWithGrip;ShowInTaskbar=false;WindowStartupLocation=WindowStartupLocation.CenterScreen;Background=System.Windows.Media.Brushes.Transparent;ThemeControls.Install(this);
            var root=new StackPanel{Margin=new Thickness(24)};var heading=new DockPanel();var close=Visuals.Button("✕",Close);DockPanel.SetDock(close,Dock.Right);heading.Children.Add(close);heading.Children.Add(Visuals.Text("Мини значки",24));heading.MouseLeftButtonDown+=delegate{try{DragMove();}catch(InvalidOperationException){}};root.Children.Add(heading);root.Children.Add(Visuals.Text("Версия "+ProductInfo.Version+" · изменения сохраняются кнопкой «Применить»",12,"Hub.Muted"));
            foreach(var monitor in topology.Monitors){monitors.Items.Add(monitor);if(monitor.Id==config.Monitor)monitors.SelectedItem=monitor;}if(monitors.SelectedIndex<0&&monitors.Items.Count>0)monitors.SelectedIndex=0;
            Add(root,"Экран рядом с панелью задач",monitors);size.Text=config.IconSize.ToString();Add(root,"Размер значков (16–32)",size);columns.Text=config.Columns.ToString();Add(root,"Значков в строке (1–8)",columns);
            windows.IsChecked=config.ShowWindowsTray;windows.Margin=new Thickness(0,16,0,8);windows.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");root.Children.Add(windows);root.Children.Add(Visuals.Text("Сторонние значки открываются в родном меню Windows. Это сохраняет их действия; перенос в наше окно не имитируется ярлыками.",12,"Hub.Muted"));
            root.Children.Add(Visuals.Button("Оформление через HUB",openHub));root.Children.Add(Visuals.Button("Применить",Apply));root.Children.Add(Visuals.Text("Порядок меняется перетаскиванием в мини-меню.\nShift + правый клик по значку — вернуть в системный трей.\nОбратный перенос — через меню значка или перетаскиванием на ребро ›.",12,"Hub.Muted"));status.Margin=new Thickness(0,12,0,0);root.Children.Add(status);surface=new ThemedSurface(new ScrollViewer{Content=root,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});Content=surface;ApplyTheme(theme);
        }
        static void Add(Panel root,string label,Control value){var text=Visuals.Text(label);text.Margin=new Thickness(0,16,0,5);root.Children.Add(text);root.Children.Add(value);}
        void Apply(){int s,c;if(!int.TryParse(size.Text,out s)||s<16||s>32||!int.TryParse(columns.Text,out c)||c<1||c>8){status.Text="Размер значков: 16–32. Столбцы: 1–8.";return;}var selected=monitors.SelectedItem as MonitorInfo;if(selected==null){status.Text="Выберите экран.";return;}config.Monitor=selected.Id;config.IconSize=s;config.Columns=c;config.ShowWindowsTray=windows.IsChecked==true;save();status.Text="Настройки сохранены.";}
        internal void ApplyTheme(ThemeProfile theme){surface.Apply(this,theme);}
    }
}
