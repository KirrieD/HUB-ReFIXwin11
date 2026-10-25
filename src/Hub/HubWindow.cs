using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using ReFix.Shared;

namespace ReFix.Hub
{
    internal sealed class HubWindow : Window
    {
        private readonly ThemeStore themes;
        private readonly ModuleManager manager;
        private readonly StackPanel cards=new StackPanel();
        private readonly TextBlock notice=Ui.Text("Локальный каталог готов",11,"Hub.Muted");
        private readonly TextBlock summary=Ui.Text("",12,"Hub.Muted");
        private readonly TextBox search=new TextBox { MinWidth=180,MaxWidth=300,Margin=new Thickness(24,0,0,0),ToolTip="Поиск расширений и фиксов" };
        private readonly DispatcherTimer statusTimer;
        private readonly Border frame;
        private readonly Border image;
        private readonly List<ModuleCard> views=new List<ModuleCard>();
        private StyleWindow editor;
        private bool exiting,refreshing;
        private string filter="all";
        internal HubWindow(ThemeStore store,ModuleManager modules)
        {
            themes=store; manager=modules; Title="HUB ReFIX — расширения Windows 11";
            Width=1060; Height=720; MinWidth=840; MinHeight=480; WindowStartupLocation=WindowStartupLocation.CenterScreen;
            WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=System.Windows.Media.Brushes.Transparent;
            ResizeMode=ResizeMode.CanResizeWithGrip; FontFamily=new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI");
            Ui.Styles(this);
            frame=new Border { Margin=new Thickness(18),CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1) };
            frame.SetResourceReference(Border.BackgroundProperty,"Hub.Background"); frame.SetResourceReference(Border.BorderBrushProperty,"Hub.Border"); Content=frame;
            Grid layers=new Grid(); frame.Child=layers;
            image=new Border { IsHitTestVisible=false }; image.SetResourceReference(Border.BackgroundProperty,"Hub.Image"); layers.Children.Add(image);
            Grid shell=new Grid(); shell.RowDefinitions.Add(new RowDefinition { Height=new GridLength(56) }); shell.RowDefinitions.Add(new RowDefinition()); shell.RowDefinitions.Add(new RowDefinition { Height=new GridLength(40) }); layers.Children.Add(shell);
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(184) }); shell.ColumnDefinitions.Add(new ColumnDefinition());
            Grid title=TitleBar(); Grid.SetColumnSpan(title,2); shell.Children.Add(title);
            StackPanel nav=new StackPanel { Margin=new Thickness(16,20,12,12) }; Grid.SetRow(nav,1); shell.Children.Add(nav);
            nav.Children.Add(Ui.Text("Расширения\nи фиксы",18,"Hub.Foreground"));
            TextBlock intro=Ui.Text("Небольшие программы\nв одном месте",12,"Hub.Muted"); intro.Margin=new Thickness(0,10,0,26); nav.Children.Add(intro);
            AddNav(nav,"Все инструменты","all"); AddNav(nav,"Установленные","installed"); AddNav(nav,"Расширения","process"); AddNav(nav,"Фиксы Windows","fix");
            Border divider=new Border { Height=1,Margin=new Thickness(0,20,0,16) }; divider.SetResourceReference(Border.BackgroundProperty,"Hub.Border"); nav.Children.Add(divider);
            Button style=Ui.Button("Оформление",OpenStyles,false); style.HorizontalContentAlignment=HorizontalAlignment.Left; nav.Children.Add(style);
            Button refresh=Ui.Button("Обновить каталог",delegate { Run(RefreshCatalog); },false); refresh.Margin=new Thickness(0,8,0,0); nav.Children.Add(refresh);
            CheckBox autorun=new CheckBox { Content="Автозапуск",IsChecked=Startup.Enabled,Margin=new Thickness(2,18,0,4) };
            autorun.Click+=delegate { try { Startup.Enabled=autorun.IsChecked==true; } catch(Exception ex) { SetNotice(ex.Message); autorun.IsChecked=Startup.Enabled; } }; nav.Children.Add(autorun);
            TextBlock shortcut=Ui.Text("Ctrl + Alt + H\nКрестик скрывает HUB",11,"Hub.Muted"); shortcut.Margin=new Thickness(2,14,0,0); nav.Children.Add(shortcut);
            Grid main=new Grid { Margin=new Thickness(12,18,22,0) }; main.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition { Height=new GridLength(32) }); main.RowDefinitions.Add(new RowDefinition()); Grid.SetRow(main,1); Grid.SetColumn(main,1); shell.Children.Add(main);
            DockPanel heading=new DockPanel { Margin=new Thickness(0,0,0,12) };
            search.HorizontalAlignment=HorizontalAlignment.Right; DockPanel.SetDock(search,Dock.Right); heading.Children.Add(search); heading.Children.Add(Ui.Text("Инструменты",25,"Hub.Foreground")); main.Children.Add(heading);
            System.Windows.Automation.AutomationProperties.SetName(search,"Поиск инструментов"); search.TextChanged+=delegate { Filter(); };
            Grid.SetRow(summary,1); main.Children.Add(summary);
            ScrollViewer scroll=new ScrollViewer { Content=cards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0) }; Grid.SetRow(scroll,2); main.Children.Add(scroll);
            DockPanel footer=new DockPanel { Margin=new Thickness(20,8,22,6) }; Grid.SetRow(footer,2); Grid.SetColumnSpan(footer,2); shell.Children.Add(footer);
            TextBlock repo=Ui.Text("KirrieD / HUB-ReFIXwin11",11,"Hub.Muted"); repo.HorizontalAlignment=HorizontalAlignment.Right; DockPanel.SetDock(repo,Dock.Right); footer.Children.Add(repo); footer.Children.Add(notice);
            statusTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2) }; statusTimer.Tick+=delegate { Run(RefreshStates); };
            IsVisibleChanged+=delegate { if(IsVisible) { statusTimer.Start(); Run(RefreshStates); } else statusTimer.Stop(); };
            Closing+=delegate(object sender,CancelEventArgs e) { if(!exiting) { e.Cancel=true; HideHub(); } };
            KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Escape) HideHub(); };
            themes.Changed+=ThemeChanged; manager.Changed+=ModulesChanged;
            Closed+=delegate { statusTimer.Stop(); themes.Changed-=ThemeChanged; manager.Changed-=ModulesChanged; foreach(ModuleCard card in views) card.Dispose(); };
            SizeChanged+=delegate { layers.Clip=new System.Windows.Media.RectangleGeometry(new Rect(0,0,layers.ActualWidth,layers.ActualHeight),frame.CornerRadius.TopLeft,frame.CornerRadius.TopLeft); ApplyTheme(); };
            ApplyTheme(); Rebuild();
        }
        private Grid TitleBar()
        {
            Grid title=new Grid { Margin=new Thickness(20,0,12,0),Background=System.Windows.Media.Brushes.Transparent };
            title.ColumnDefinitions.Add(new ColumnDefinition()); title.ColumnDefinitions.Add(new ColumnDefinition { Width=GridLength.Auto });
            StackPanel brand=new StackPanel { Orientation=Orientation.Horizontal,VerticalAlignment=VerticalAlignment.Center };
            TextBlock mark=Ui.Text("◈",23,"Hub.Accent"); mark.Margin=new Thickness(0,0,10,0); brand.Children.Add(mark);
            brand.Children.Add(Ui.Text("HUB ReFIX",17,"Hub.Foreground")); title.Children.Add(brand);
            title.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e) { if(e.ClickCount==1) try { DragMove(); } catch(InvalidOperationException) { } };
            Button close=Ui.Button("✕",HideHub,false); close.BorderThickness=new Thickness(0); close.Width=34; close.Height=30; close.Padding=new Thickness(0); close.ToolTip="Скрыть в трей"; Grid.SetColumn(close,1); title.Children.Add(close); return title;
        }
        private void AddNav(Panel panel,string label,string value)
        {
            Button b=Ui.Button(label,delegate { filter=value; Filter(); },false); b.Margin=new Thickness(0,0,0,7); b.HorizontalContentAlignment=HorizontalAlignment.Left; panel.Children.Add(b);
        }
        private async void Run(Func<Task> action)
        {
            try { await action(); } catch(Exception ex) { SetNotice(ex.Message); }
        }
        private async Task RefreshCatalog()
        {
            SetNotice("Проверка каталога GitHub…");
            await manager.RefreshCatalogAsync(); Rebuild(); SetNotice("Каталог обновлён");
        }
        private async Task RefreshStates()
        {
            if(refreshing || !IsVisible) return; refreshing=true;
            try { await manager.RefreshStatesAsync(); Summary(); } finally { refreshing=false; }
        }
        private void ModulesChanged(object sender,EventArgs e)
        {
            if(Dispatcher.HasShutdownStarted) return;
            Dispatcher.BeginInvoke(new Action(delegate
            {
                IList<ModuleView> snapshot=manager.Modules;
                if(views.Count!=snapshot.Count || views.Where((card,i)=>card.View!=snapshot[i]).Any()) Rebuild();
                else { foreach(ModuleCard card in views) card.Update(); Summary(); }
            }));
        }
        private void Rebuild()
        {
            foreach(ModuleCard card in views) card.Dispose(); views.Clear(); cards.Children.Clear();
            foreach(ModuleView module in manager.Modules)
            {
                ModuleCard card=new ModuleCard(module,manager,SetNotice); views.Add(card); cards.Children.Add(card);
            }
            if(views.Count==0) cards.Children.Add(Ui.Text("В каталоге пока нет инструментов. Обновите каталог или добавьте пакет в репозиторий.",15,"Hub.Muted"));
            foreach(ModuleCard card in views)card.ApplyPreview(themes.Current);
            Filter();
        }
        private void Filter()
        {
            string q=search.Text.Trim();
            foreach(ModuleCard card in views)
            {
                bool visible=(filter=="all" || filter=="installed" && card.View.State!="NO" || card.View.Manifest.Kind==filter) &&
                    (q.Length==0 || (card.View.Manifest.Name+" "+card.View.Manifest.Description).IndexOf(q,StringComparison.CurrentCultureIgnoreCase)>=0);
                card.Visibility=visible?Visibility.Visible:Visibility.Collapsed;
            }
            Summary();
        }
        private void Summary()
        {
            summary.Text=manager.Modules.Count+" в каталоге   /   "+manager.Modules.Count(v=>v.State=="ON")+" включено   /   "+manager.Modules.Count(v=>v.State=="NO")+" доступно для установки";
        }
        internal void SetNotice(string text) { if(!Dispatcher.CheckAccess()) { Dispatcher.BeginInvoke(new Action(delegate { SetNotice(text); })); return; } notice.Text=text; notice.ToolTip=text; }
        private void ThemeChanged(object sender,EventArgs e) { if(!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(delegate{ApplyTheme();if(!string.IsNullOrEmpty(themes.LastError))SetNotice("Не удалось сохранить оформление: "+themes.LastError);})); }
        private void ApplyTheme() { Ui.Apply(this,themes.Current,frame,image);foreach(ModuleCard card in views)card.ApplyPreview(themes.Current); }
        internal void OpenStyles()
        {
            if(editor==null) { editor=new StyleWindow(themes); editor.Owner=this;System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(editor); editor.Closed+=delegate { editor=null; }; }
            editor.Show(); editor.Activate();
        }
        internal StyleWindow StyleEditor { get { return editor; } }
        internal void ShowHub() { Show(); WindowState=WindowState.Normal; Native.SetForegroundWindow(new WindowInteropHelper(this).Handle); Activate(); }
        internal void HideHub() { if(editor!=null) editor.Hide(); Hide(); }
        internal void Exit() { exiting=true; if(editor!=null) editor.Close(); Close(); }
        internal bool StatusTimerRunning { get { return statusTimer.IsEnabled; } }
    }

    internal sealed class ModuleCard : Border,IDisposable
    {
        internal readonly ModuleView View;
        private readonly ModuleManager manager;
        private readonly Action<string> notify;
        private readonly TextBlock state,details,version;
        private readonly Button primary,open,update;
        private readonly ModulePreview previewWindow;
        internal ModuleCard(ModuleView module,ModuleManager service,Action<string> notice)
        {
            View=module; manager=service; notify=notice;
            Padding=new Thickness(16); Margin=new Thickness(0,0,0,12); BorderThickness=new Thickness(1); CornerRadius=new CornerRadius(10);
            SetResourceReference(BackgroundProperty,"Hub.Surface"); SetResourceReference(BorderBrushProperty,"Hub.Border");
            Grid layout=new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(236) }); Child=layout;
            StackPanel text=new StackPanel { Margin=new Thickness(0,0,16,0) }; layout.Children.Add(text);
            DockPanel heading=new DockPanel(); state=Ui.Text("NO",12,"Hub.Muted"); state.FontWeight=FontWeights.Bold; state.Margin=new Thickness(12,3,0,0); DockPanel.SetDock(state,Dock.Right); heading.Children.Add(state); heading.Children.Add(Ui.Text(module.Manifest.Name,18,"Hub.Foreground")); text.Children.Add(heading);
            version=Ui.Text("",10,"Hub.Muted"); version.Margin=new Thickness(0,4,0,9); text.Children.Add(version);
            text.Children.Add(Ui.Text(module.Manifest.Description,12,"Hub.Muted"));
            details=Ui.Text("",11,"Hub.Muted"); details.Margin=new Thickness(0,7,0,0); text.Children.Add(details);
            WrapPanel actions=new WrapPanel { Margin=new Thickness(0,12,0,0) }; text.Children.Add(actions);
            primary=Ui.Button("Установить",delegate { InvokePrimary(); },true); actions.Children.Add(primary);
            open=Ui.Button("Открыть",delegate { Run(delegate { return manager.ShowAsync(View); }); },false); open.Margin=new Thickness(7,0,0,0); actions.Children.Add(open);
            update=Ui.Button("Обновить",delegate { Run(delegate { return manager.InstallAsync(View); }); },false); update.Margin=new Thickness(7,0,0,0); actions.Children.Add(update);
            StackPanel preview=new StackPanel { VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right };
            previewWindow=new ModulePreview(module.Manifest);preview.Children.Add(previewWindow); TextBlock caption=Ui.Text("Предпросмотр оформления",10,"Hub.Muted"); caption.HorizontalAlignment=HorizontalAlignment.Center; caption.Margin=new Thickness(0,6,0,0); preview.Children.Add(caption); Grid.SetColumn(preview,1); layout.Children.Add(preview);
            View.PropertyChanged+=Changed; Update();
        }
        private void Changed(object sender,PropertyChangedEventArgs e) { if(!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Update)); }
        internal void Update()
        {
            state.Text=View.State; state.SetResourceReference(TextBlock.ForegroundProperty,View.State=="ON"?"Hub.Accent":"Hub.Muted");
            state.ToolTip=View.State=="NO"?"Не установлено":View.State=="ON"?"Состояние проверено: работает":"Установлено, выключено";
            version.Text="v"+View.Manifest.Version+(string.IsNullOrEmpty(View.InstalledVersion)?"":"  ·  установлена "+View.InstalledVersion);
            details.Text=View.Detail??"";
            primary.Content=View.IsBusy?"Подождите…":View.State=="NO"?"Установить":View.State=="ON"?"Выключить":View.Manifest.Kind=="fix"?"Применить":"Включить";
            primary.IsEnabled=!View.IsBusy; open.IsEnabled=!View.IsBusy;
            open.Visibility=View.State=="ON" && View.Manifest.Kind!="fix"?Visibility.Visible:Visibility.Collapsed;
            update.Visibility=!string.IsNullOrEmpty(View.InstalledVersion) && View.InstalledVersion!=View.Manifest.Version?Visibility.Visible:Visibility.Collapsed; update.IsEnabled=!View.IsBusy && View.State!="ON";
        }
        private void InvokePrimary() { Run(delegate { return View.State=="NO"?manager.InstallAsync(View):View.State=="ON"?manager.StopAsync(View):manager.StartAsync(View); }); }
        internal void ApplyPreview(ThemeProfile profile){previewWindow.ApplyTheme(profile);}
        private async void Run(Func<Task> action) { try { await action(); notify("Готово: "+View.Manifest.Name); } catch(Exception ex) { notify(ex.Message); } finally { Update(); } }
        public void Dispose() { View.PropertyChanged-=Changed; }
    }
}
