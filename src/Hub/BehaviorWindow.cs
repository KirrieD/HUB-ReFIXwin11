using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReFix.Shared;
namespace ReFix.Hub
{
    internal sealed class BehaviorWindow:Window
    {
        readonly ThemeStore themes;readonly ControlsStore controls;
        readonly Func<bool> readStartup;readonly Action<bool> writeStartup;
        readonly Border frame,image;readonly List<CheckBox> choices=new List<CheckBox>();
        readonly TextBlock notice=Ui.Text("Изменения сохраняются сразу.",12,"Hub.Muted");bool closed;
        internal BehaviorWindow(ThemeStore themes,ControlsStore controls):this(themes,controls,delegate{return Startup.Enabled;},delegate(bool enabled){Startup.Enabled=enabled;}){}
        internal BehaviorWindow(ThemeStore themes,ControlsStore controls,Func<bool> readStartup,Action<bool> writeStartup)
        {
            this.themes=themes;this.controls=controls;this.readStartup=readStartup;this.writeStartup=writeStartup;
            Title="HUB ReFIX — поведение";Width=560;Height=530;MinWidth=420;MinHeight=430;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
            Ui.Styles(this);frame=new Border{Margin=new Thickness(18),BorderThickness=new Thickness(1)};Content=frame;
            Grid layers=new Grid();frame.Child=layers;image=new Border{IsHitTestVisible=false};layers.Children.Add(image);
            StackPanel content=new StackPanel{Margin=new Thickness(24)};
            ScrollViewer scroll=new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto};layers.Children.Add(scroll);
            DockPanel title=new DockPanel{Margin=new Thickness(0,0,0,20),Background=Brushes.Transparent};
            Button close=Ui.Button("✕",Close,false);DockPanel.SetDock(close,Dock.Right);title.Children.Add(close);title.Children.Add(Ui.Text("Поведение",23,"Hub.Foreground"));content.Children.Add(title);
            title.MouseLeftButtonDown+=delegate{try{DragMove();}catch(InvalidOperationException){}};
            content.Children.Add(Ui.Text("Настройки поведения работы HUB и расширений",13,"Hub.Muted"));
            Add(content,"Скрывать Микшер",delegate(ControlsProfile p,bool v){p.AutoHideMixer=v;});
            Add(content,"Скрывать Меню",delegate(ControlsProfile p,bool v){p.AutoHideTaskMenu=v;});
            Add(content,"Скрывать HUB",delegate(ControlsProfile p,bool v){p.AutoHideHub=v;});
            Add(content,"Автозапуск HUB",null);
            Add(content,"Автозапуск Микшер",delegate(ControlsProfile p,bool v){p.StartMixerWithHub=v;});
            Add(content,"Автозапуск Меню",delegate(ControlsProfile p,bool v){p.StartTaskMenuWithHub=v;});
            notice.Margin=new Thickness(0,22,0,0);content.Children.Add(notice);
            themes.Changed+=Changed;controls.Changed+=Changed;
            Closed+=delegate{closed=true;themes.Changed-=Changed;controls.Changed-=Changed;};
            SizeChanged+=delegate{Ui.Apply(this,themes.Current,frame,image);};Refresh();
        }
        void Add(Panel parent,string text,Action<ControlsProfile,bool> update)
        {
            var box=new CheckBox{Content=text,Margin=new Thickness(0,17,0,0)};choices.Add(box);parent.Children.Add(box);
            box.Click+=delegate{try{bool value=box.IsChecked==true;if(update==null)writeStartup(value);else{var p=controls.Current;update(p,value);controls.Save(p);}notice.Text="Изменения сохранены.";}catch(Exception ex){notice.Text=ex.Message;}Refresh();};
        }
        void Refresh(){var p=controls.Current;choices[0].IsChecked=p.AutoHideMixer;choices[1].IsChecked=p.AutoHideTaskMenu;choices[2].IsChecked=p.AutoHideHub;choices[3].IsChecked=readStartup();choices[4].IsChecked=p.StartMixerWithHub;choices[5].IsChecked=p.StartTaskMenuWithHub;Ui.Apply(this,themes.Current,frame,image);}
        void Changed(object sender,EventArgs e){if(!closed&&!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{if(!closed)Refresh();}));}
    }
}
