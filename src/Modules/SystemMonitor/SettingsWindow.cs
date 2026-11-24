using System;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReFix.Shared;
using ReFix.Modules;

namespace ReFix.SystemMonitor
{
    internal sealed class SettingsWindow:Window
    {
        readonly ThemedSurface surface;
        readonly MonitorConfig value;
        readonly TextBlock status=new TextBlock{TextWrapping=TextWrapping.Wrap};
        internal SettingsWindow(ThemeProfile theme,MonitorConfig config,Action<MonitorConfig> save)
        {
            value=new JavaScriptSerializer().Deserialize<MonitorConfig>(new JavaScriptSerializer().Serialize(config));
            Title="Системный мониторинг · настройки";Width=440;Height=620;MinHeight=380;MinWidth=350;
            WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;
            ThemeControls.Install(this);
            var root=new DockPanel{Margin=new Thickness(18)};
            var header=new DockPanel();
            var close=Button("×",Close);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);
            header.Children.Add(new TextBlock{Text="Настройки мониторинга",FontSize=19,VerticalAlignment=VerticalAlignment.Center});
            header.MouseLeftButtonDown+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){if(e.OriginalSource is TextBlock)DragMove();};
            DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
            var bottom=new StackPanel();bottom.Children.Add(status);
            bottom.Children.Add(Button("Применить",delegate{try{value.Normalize();save(value);status.Text="Настройки сохранены.";}catch(Exception ex){status.Text=ex.Message;}}));
            DockPanel.SetDock(bottom,Dock.Bottom);root.Children.Add(bottom);
            var body=new StackPanel();
            Number(body,"Интервал, с",new[]{1,2,3,5,10},value.IntervalSeconds,n=>value.IntervalSeconds=n);
            Number(body,"История, с",new[]{30,60,120,300,600,1200},value.HistorySeconds,n=>value.HistorySeconds=n);
            Check(body,"Собирать историю, когда все окна скрыты",value.BackgroundHistory,b=>value.BackgroundHistory=b);
            Check(body,"Поверх других окон",value.Topmost,b=>value.Topmost=b);
            Slider(body,"Размер виджета",.7,2,value.Scale,n=>value.Scale=n);
            Slider(body,"Прозрачность окна",.3,1,value.Opacity,n=>value.Opacity=n);
            body.Children.Add(new TextBlock{Text="Строки и цвета графиков · HEX",Margin=new Thickness(0,16,0,6)});
            foreach(var kind in new[]{"cpu","gpu","memory","disk"})
            {
                string label=kind=="cpu"?"ЦП":kind=="gpu"?"GPU":kind=="memory"?"ОЗУ":"Диски";
                Check(body,label,value.VisibleKinds.Contains(kind),b=>{value.VisibleKinds.Remove(kind);if(b)value.VisibleKinds.Add(kind);});
                string color;value.Colors.TryGetValue(kind,out color);
                var field=new TextBox{Text=color??"#42A5FF",Margin=new Thickness(18,0,0,4),MaxLength=7};
                field.TextChanged+=delegate{if(System.Text.RegularExpressions.Regex.IsMatch(field.Text,"^#[0-9a-fA-F]{6}$"))value.Colors[kind]=field.Text;};
                body.Children.Add(field);
            }
            body.Children.Add(new TextBlock{Text="Температуры поступают от доступных аппаратных датчиков. Если источник недоступен, показано «Нет данных». При работающем LibreHardwareMonitor его датчики читаются через WMI.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,12,0,0),Opacity=.7});
            root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
            surface=new ThemedSurface(root);Content=surface;ApplyTheme(theme);
        }
        internal void ApplyTheme(ThemeProfile theme){surface.Apply(this,theme);}
        static Button Button(string text,Action click){var b=new Button{Content=text,Padding=new Thickness(10,5,10,5),Margin=new Thickness(0,4,0,4)};b.Click+=delegate{click();};return b;}
        static void Check(Panel parent,string text,bool value,Action<bool> changed)
        {
            var box=new CheckBox{Content=text,IsChecked=value,Margin=new Thickness(0,8,0,4)};parent.Children.Add(box);box.Click+=delegate{changed(box.IsChecked==true);};
        }
        static void Number(Panel parent,string title,int[] options,int current,Action<int> changed)
        {
            parent.Children.Add(new TextBlock{Text=title,Margin=new Thickness(0,10,0,4)});
            var box=new ComboBox{ItemsSource=options,SelectedItem=current};parent.Children.Add(box);box.SelectionChanged+=delegate{if(box.SelectedItem is int)changed((int)box.SelectedItem);};
        }
        static void Slider(Panel parent,string title,double min,double max,double current,Action<double> changed)
        {
            var text=new TextBlock{Text=title+" · "+current.ToString("0.##"),Margin=new Thickness(0,10,0,4)};parent.Children.Add(text);
            var slider=new System.Windows.Controls.Slider{Minimum=min,Maximum=max,Value=current};parent.Children.Add(slider);slider.ValueChanged+=delegate{text.Text=title+" · "+slider.Value.ToString("0.##");changed(slider.Value);};
        }
    }
}
