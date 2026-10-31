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
        readonly Func<bool> readStartup;readonly Action<bool> writeStartup;readonly Func<string,bool> installed;
        readonly Border frame,image;readonly List<Action<ControlsProfile>> refreshers=new List<Action<ControlsProfile>>();readonly Dictionary<string,Expander> sections=new Dictionary<string,Expander>();
        readonly TextBlock notice=Ui.Text("Изменения сохраняются сразу.",12,"Hub.Muted");bool closed;
        internal BehaviorWindow(ThemeStore themes,ControlsStore controls,Func<string,bool> installed=null,Func<System.Threading.Tasks.Task> openMiniSettings=null):this(themes,controls,delegate{return Startup.Enabled;},delegate(bool enabled){Startup.Enabled=enabled;},installed,openMiniSettings){}
        internal BehaviorWindow(ThemeStore themes,ControlsStore controls,Func<bool> readStartup,Action<bool> writeStartup,Func<string,bool> installed=null,Func<System.Threading.Tasks.Task> openMiniSettings=null)
        {
            this.themes=themes;this.controls=controls;this.readStartup=readStartup;this.writeStartup=writeStartup;this.installed=installed??delegate{return true;};
            Title="HUB ReFIX — поведение";Width=590;Height=760;MinWidth=420;MinHeight=430;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
            Ui.Styles(this);frame=new Border{Margin=new Thickness(18),BorderThickness=new Thickness(1)};Content=frame;
            Grid layers=new Grid();frame.Child=layers;image=new Border{IsHitTestVisible=false};layers.Children.Add(image);
            var root=new DockPanel{Margin=new Thickness(24)};layers.Children.Add(root);
            DockPanel title=new DockPanel{Margin=new Thickness(0,0,0,18),Background=Brushes.Transparent};
            Button close=Ui.Button("✕",Close,false);DockPanel.SetDock(close,Dock.Right);title.Children.Add(close);title.Children.Add(Ui.Text("Поведение",23,"Hub.Foreground"));DockPanel.SetDock(title,Dock.Top);root.Children.Add(title);
            title.MouseLeftButtonDown+=delegate{try{DragMove();}catch(InvalidOperationException){}};
            notice.Margin=new Thickness(0,18,0,0);DockPanel.SetDock(notice,Dock.Bottom);root.Children.Add(notice);
            var content=new StackPanel();content.Children.Add(Ui.Text("Откройте раздел нужного приложения.",13,"Hub.Muted"));root.Children.Add(new ScrollViewer{Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            var hub=Section(content,"hub","Поведение HUB",true);
            Add(hub,"Скрывать HUB",p=>p.AutoHideHub,(p,v)=>p.AutoHideHub=v);
            Add(hub,"Автозапуск HUB",p=>readStartup(),null);
            var mixer=Section(content,"quiet-mixer","Поведение Микшера",false);
            Add(mixer,"Скрывать Микшер",p=>p.AutoHideMixer,(p,v)=>p.AutoHideMixer=v);
            Add(mixer,"Автозапуск Микшер",p=>p.StartMixerWithHub,(p,v)=>p.StartMixerWithHub=v);
            var menu=Section(content,"task-menu","Поведение Меню задач",false);
            Add(menu,"Скрывать Меню",p=>p.AutoHideTaskMenu,(p,v)=>p.AutoHideTaskMenu=v);
            Add(menu,"Автозапуск Меню",p=>p.StartTaskMenuWithHub,(p,v)=>p.StartTaskMenuWithHub=v);
            Label(menu,"Расположение поверх окон");
            Layer(menu,"Поверх всех окон (стандарт)",TaskMenuLayer.AboveAll);
            Layer(menu,"Частично",TaskMenuLayer.ExceptFullscreen);
            Layer(menu,"Ниже всех окон",TaskMenuLayer.BelowWindows);
            menu.Children.Add(Ui.Text("«Частично» уступает развёрнутым и полноэкранным приложениям на этом мониторе.",11,"Hub.Muted"));
            Label(menu,"Грани");
            Choice(menu,"Показать грани","menu-edges",p=>p.ShowTaskMenuEdges,(p)=>p.ShowTaskMenuEdges=true);
            Choice(menu,"Убрать грани","menu-edges",p=>!p.ShowTaskMenuEdges,(p)=>p.ShowTaskMenuEdges=false);
            menu.Children.Add(Ui.Text("Скрытая грань остаётся зоной наведения поверх окон. Открытое меню соблюдает выбранный режим наложения.",11,"Hub.Muted"));
            var mini=Section(content,"mini-icons","Поведение Мини значков",false);
            Add(mini,"Скрывать мини-меню",p=>p.AutoHideMiniIcons,(p,v)=>p.AutoHideMiniIcons=v);
            Add(mini,"Автозапуск Мини значки",p=>p.StartMiniIconsWithHub,(p,v)=>p.StartMiniIconsWithHub=v);
            var miniSettings=Ui.Button("Открыть настройки",delegate{},false);miniSettings.Margin=new Thickness(0,14,0,0);miniSettings.IsEnabled=openMiniSettings!=null;mini.Children.Add(miniSettings);miniSettings.Click+=async delegate{miniSettings.IsEnabled=false;try{if(openMiniSettings!=null)await openMiniSettings();}catch(Exception ex){notice.Text="Не удалось открыть настройки: "+ex.Message;}finally{miniSettings.IsEnabled=openMiniSettings!=null;}};
            themes.Changed+=Changed;controls.Changed+=Changed;Activated+=delegate{Refresh();};
            Closed+=delegate{closed=true;themes.Changed-=Changed;controls.Changed-=Changed;};
            SizeChanged+=delegate{Ui.Apply(this,themes.Current,frame,image,true);};Refresh();
        }
        StackPanel Section(Panel parent,string id,string title,bool expanded){var body=new StackPanel{Margin=new Thickness(12,4,8,14)};var section=new Expander{Header=Ui.Text(title,15,"Hub.Foreground"),Content=body,IsExpanded=expanded,Padding=new Thickness(10),Margin=new Thickness(0,12,0,0)};section.SetResourceReference(Control.BackgroundProperty,"Hub.Surface");sections[id]=section;parent.Children.Add(section);return body;}
        static void Label(Panel parent,string text){var label=Ui.Text(text,13,"Hub.Foreground");label.FontWeight=FontWeights.SemiBold;label.Margin=new Thickness(0,18,0,5);parent.Children.Add(label);}
        void Save(Action<ControlsProfile> update){try{var p=controls.Current;update(p);controls.Save(p);notice.Text="Изменения сохранены.";}catch(Exception ex){notice.Text=ex.Message;}Refresh();}
        void Add(Panel parent,string text,Func<ControlsProfile,bool> read,Action<ControlsProfile,bool> update){var box=new CheckBox{Content=text,Margin=new Thickness(0,10,0,0)};parent.Children.Add(box);refreshers.Add(p=>box.IsChecked=read(p));box.Click+=delegate{if(update==null){try{writeStartup(box.IsChecked==true);notice.Text="Изменения сохранены.";}catch(Exception ex){notice.Text=ex.Message;}Refresh();}else Save(p=>update(p,box.IsChecked==true));};}
        void Layer(Panel parent,string text,TaskMenuLayer mode){Choice(parent,text,"menu-layer",p=>p.TaskMenuLayer==mode,p=>p.TaskMenuLayer=mode);}
        void Choice(Panel parent,string text,string group,Func<ControlsProfile,bool> read,Action<ControlsProfile> write){var choice=new RadioButton{Content=text,GroupName=group,Margin=new Thickness(0,8,0,8)};choice.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");parent.Children.Add(choice);refreshers.Add(p=>choice.IsChecked=read(p));choice.Click+=delegate{if(choice.IsChecked==true)Save(write);};}
        void Refresh(){if(closed)return;var p=controls.Current;foreach(var refresh in refreshers)refresh(p);foreach(var item in sections)item.Value.Visibility=item.Key=="hub"||installed(item.Key)?Visibility.Visible:Visibility.Collapsed;Ui.Apply(this,themes.Current,frame,image,true);}
        void Changed(object sender,EventArgs e){if(!closed&&!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{if(!closed)Refresh();}));}
    }
}
