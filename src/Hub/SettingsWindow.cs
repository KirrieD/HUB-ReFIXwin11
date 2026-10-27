using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReFix.Shared;
namespace ReFix.Hub
{
    internal sealed class SettingsWindow:Window
    {
        readonly ThemeStore themes;readonly Border frame,image;bool closed;
        internal string Selection{get;private set;}
        internal SettingsWindow(ThemeStore themes)
        {
            this.themes=themes;Title="HUB ReFIX — настройки";Width=590;Height=380;ResizeMode=ResizeMode.NoResize;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
            Ui.Styles(this);frame=new Border{Margin=new Thickness(18),BorderThickness=new Thickness(1)};Content=frame;
            Grid layers=new Grid();frame.Child=layers;image=new Border{IsHitTestVisible=false};layers.Children.Add(image);
            StackPanel panel=new StackPanel{Margin=new Thickness(24)};layers.Children.Add(panel);
            DockPanel title=new DockPanel{Margin=new Thickness(0,0,0,18),Background=Brushes.Transparent};Button close=Ui.Button("✕",Close,false);DockPanel.SetDock(close,Dock.Right);title.Children.Add(close);title.Children.Add(Ui.Text("Настройки",23,"Hub.Foreground"));panel.Children.Add(title);
            title.MouseLeftButtonDown+=delegate{try{DragMove();}catch(InvalidOperationException){}};
            Add(panel,"Управление","Настройка раскладки клавиатуры","controls");
            Add(panel,"Оформление","Настройки оформления HUB и расширений","styles");
            Add(panel,"Поведение","Настройки поведения работы HUB и расширений","behavior");
            themes.Changed+=Changed;Closed+=delegate{closed=true;themes.Changed-=Changed;};SizeChanged+=delegate{Apply();};Apply();
        }
        void Add(Panel panel,string title,string subtitle,string selection){Button b=Ui.Button(title+" ›\n"+subtitle,delegate{Selection=selection;DialogResult=true;},false);b.HorizontalContentAlignment=HorizontalAlignment.Left;b.Margin=new Thickness(0,0,0,12);b.Padding=new Thickness(14,10,14,10);panel.Children.Add(b);}
        void Apply(){Ui.Apply(this,themes.Current,frame,image);}
        void Changed(object sender,EventArgs e){if(!closed&&!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{if(!closed)Apply();}));}
    }
}
