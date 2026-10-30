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
        private readonly ControlsStore controls;
        private readonly bool ownsControls;
        private readonly TextBlock shortcut=Ui.Text("",11,"Hub.Muted");
        private readonly StackPanel cards=new StackPanel();
        private readonly TextBlock notice=Ui.Text("Локальный каталог готов",13,"Hub.Tertiary");
        private readonly TextBlock summary=Ui.Text("",12,"Hub.Muted");
        private readonly TextBox search=new TextBox { MinWidth=180,MaxWidth=300,Margin=new Thickness(24,0,0,0),ToolTip="Поиск расширений и фиксов" };
        private readonly DispatcherTimer statusTimer;
        private readonly Border frame;
        private readonly Border image;
        private readonly List<ModuleCard> views=new List<ModuleCard>();
        private StyleWindow editor;
        private ControlsWindow controlsEditor;
        private BehaviorWindow behaviorEditor;
        private LinksWindow linksEditor;private MixerLayoutWindow mixerEditor;
        private bool exiting,refreshing;
        private string filter="all";
        internal HubWindow(ThemeStore store,ModuleManager modules,ControlsStore bindings=null,Action checkUpdates=null)
        {
            themes=store; manager=modules; Title="HUB ReFIX — расширения Windows 11";
            ownsControls=bindings==null;controls=bindings??new ControlsStore(Paths.Root);
            Width=1060; Height=720; MinWidth=840; MinHeight=480; WindowStartupLocation=WindowStartupLocation.CenterScreen;
            WindowStyle=WindowStyle.None; AllowsTransparency=true; Background=System.Windows.Media.Brushes.Transparent;
            ResizeMode=ResizeMode.CanResizeWithGrip; FontFamily=new System.Windows.Media.FontFamily("Segoe UI Variable, Segoe UI");
            Ui.Styles(this);
            frame=new Border { Margin=new Thickness(18),CornerRadius=new CornerRadius(14),BorderThickness=new Thickness(1) };
            frame.SetResourceReference(Border.BackgroundProperty,"Hub.Background"); frame.SetResourceReference(Border.BorderBrushProperty,"Hub.Border"); Content=frame;
            Grid layers=new Grid(); frame.Child=layers;
            image=new Border { IsHitTestVisible=false }; image.SetResourceReference(Border.BackgroundProperty,"Hub.Image"); layers.Children.Add(image);
            Grid shell=new Grid(); shell.RowDefinitions.Add(new RowDefinition { Height=new GridLength(56) }); shell.RowDefinitions.Add(new RowDefinition()); shell.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); layers.Children.Add(shell);
            shell.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(184) }); shell.ColumnDefinitions.Add(new ColumnDefinition());
            Grid title=TitleBar(); Grid.SetColumnSpan(title,2); shell.Children.Add(title);
            StackPanel nav=new StackPanel { Margin=new Thickness(16,20,12,12) };
            ScrollViewer navigation=new ScrollViewer { Content=nav,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled };Grid.SetRow(navigation,1);shell.Children.Add(navigation);
            nav.Children.Add(Ui.Text("Расширения\nи фиксы",18,"Hub.Foreground"));
            TextBlock intro=Ui.Text("Небольшие программы\nв одном месте",12,"Hub.Muted"); intro.Margin=new Thickness(0,10,0,26); nav.Children.Add(intro);
            AddNav(nav,"Все инструменты","all"); AddNav(nav,"Установленные","installed"); AddNav(nav,"Расширения","process"); AddNav(nav,"Фиксы Windows","fix");
            Border divider=new Border { Height=1,Margin=new Thickness(0,20,0,16) }; divider.SetResourceReference(Border.BackgroundProperty,"Hub.Border"); nav.Children.Add(divider);
            Button settings=Ui.Button("Настройки",OpenSettings,false);settings.HorizontalContentAlignment=HorizontalAlignment.Center;nav.Children.Add(settings);
            Button refresh=Ui.Button("Обновить каталог",delegate { Run(RefreshCatalog); },false); refresh.Margin=new Thickness(0,8,0,0); nav.Children.Add(refresh);
            shortcut.Margin=new Thickness(2,14,0,0); nav.Children.Add(shortcut);RefreshControls();
            Grid main=new Grid { Margin=new Thickness(12,18,22,0) }; main.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); main.RowDefinitions.Add(new RowDefinition { Height=new GridLength(32) }); main.RowDefinitions.Add(new RowDefinition()); Grid.SetRow(main,1); Grid.SetColumn(main,1); shell.Children.Add(main);
            DockPanel heading=new DockPanel { Margin=new Thickness(0,0,0,12) };
            search.HorizontalAlignment=HorizontalAlignment.Right; DockPanel.SetDock(search,Dock.Right); heading.Children.Add(search); heading.Children.Add(Ui.Text("Инструменты",25,"Hub.Foreground")); main.Children.Add(heading);
            System.Windows.Automation.AutomationProperties.SetName(search,"Поиск инструментов"); search.TextChanged+=delegate { Filter(); };
            Grid.SetRow(summary,1); main.Children.Add(summary);
            ScrollViewer scroll=new ScrollViewer { Content=cards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Padding=new Thickness(0,0,8,0) }; Grid.SetRow(scroll,2); main.Children.Add(scroll);
            Grid footer=new Grid { Margin=new Thickness(20,8,22,12) };footer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});footer.RowDefinitions.Add(new RowDefinition{Height=GridLength.Auto});Grid.SetRow(footer,2);Grid.SetColumnSpan(footer,2);shell.Children.Add(footer);
            DockPanel footerActions=new DockPanel();footer.Children.Add(footerActions);
            TextBlock repo=Ui.Text("v"+ProductInfo.Version+" · KirrieD / HUB-ReFIXwin11",11,"Hub.Muted");repo.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(repo,Dock.Right);footerActions.Children.Add(repo);
            if(checkUpdates!=null){Button update=Ui.Button("Проверить обновления",checkUpdates,false);update.Padding=new Thickness(10,5,10,5);update.MinHeight=28;update.FontSize=12;update.HorizontalAlignment=HorizontalAlignment.Left;footerActions.Children.Add(update);}
            Border message=new Border { Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,8,0,0),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6) };message.SetResourceReference(Border.BackgroundProperty,"Hub.NoticeBackground");message.SetResourceReference(Border.BorderBrushProperty,"Hub.Border");Grid.SetRow(message,1);footer.Children.Add(message);
            notice.FontWeight=FontWeights.SemiBold;notice.TextWrapping=TextWrapping.Wrap;notice.TextTrimming=TextTrimming.CharacterEllipsis;notice.MaxHeight=44;System.Windows.Automation.AutomationProperties.SetName(notice,"Состояние обновлений и операций");message.Child=notice;
            statusTimer=new DispatcherTimer { Interval=TimeSpan.FromSeconds(2) }; statusTimer.Tick+=delegate { Run(RefreshStates); };
            IsVisibleChanged+=delegate { if(IsVisible) { statusTimer.Start(); Run(RefreshStates); } else statusTimer.Stop(); };
            Closing+=delegate(object sender,CancelEventArgs e) { if(!exiting) { e.Cancel=true; HideHub(); } };
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e) { if(KeyChord.Parse(controls.Current.Get("hub.hide")).Matches(e.Key==Key.System?e.SystemKey:e.Key,Keyboard.Modifiers)){HideHub();e.Handled=true;} };
            themes.Changed+=ThemeChanged; manager.Changed+=ModulesChanged;
            controls.Changed+=ControlsChanged;controls.RuntimeChanged+=ControlsChanged;
            Closed+=delegate { statusTimer.Stop(); themes.Changed-=ThemeChanged; manager.Changed-=ModulesChanged;controls.Changed-=ControlsChanged;controls.RuntimeChanged-=ControlsChanged;if(ownsControls)controls.Dispose(); foreach(ModuleCard card in views) card.Dispose(); };
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
                ModuleCard card=new ModuleCard(module,manager,SetNotice,controls,themes.Root); views.Add(card); cards.Children.Add(card);
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
        internal void OpenSettings()
        {
            if(editor!=null)editor.Hide();if(controlsEditor!=null)controlsEditor.Hide();if(behaviorEditor!=null)behaviorEditor.Hide();if(linksEditor!=null)linksEditor.Hide();if(mixerEditor!=null)mixerEditor.Hide();
            var selector=new SettingsWindow(themes){Owner=this};SettingsPlacement.Place(this,selector);
            if(selector.ShowDialog()!=true)return;
            if(selector.Selection=="controls")OpenControls();else if(selector.Selection=="appearance")OpenAppearance();else if(selector.Selection=="behavior")OpenBehavior();else if(selector.Selection=="links")OpenLinks();
        }
        internal void OpenBehavior()
        {
            if(behaviorEditor==null){behaviorEditor=new BehaviorWindow(themes,controls,id=>manager.Modules.Any(m=>m.Manifest.Id==id&&!string.IsNullOrEmpty(m.InstalledVersion)));behaviorEditor.Owner=this;System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(behaviorEditor);behaviorEditor.Closed+=delegate{behaviorEditor=null;};}
            SettingsPlacement.Place(this,behaviorEditor);behaviorEditor.Show();behaviorEditor.Activate();
        }
        internal BehaviorWindow BehaviorEditor{get{return behaviorEditor;}}
        internal void OpenAppearance()
        {
            var selector=new SettingsWindow(themes,true){Owner=this};SettingsPlacement.Place(this,selector);
            if(selector.ShowDialog()!=true)return;
            if(selector.Selection=="styles")OpenStyles();else if(selector.Selection=="mixer-layout")OpenMixerLayout();
        }
        internal void OpenMixerLayout()
        {
            if(mixerEditor==null){mixerEditor=new MixerLayoutWindow(themes){Owner=this};System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(mixerEditor);mixerEditor.Closed+=delegate{mixerEditor=null;};}
            SettingsPlacement.Place(this,mixerEditor);mixerEditor.Show();mixerEditor.Activate();
        }
        internal void OpenLinks()
        {
            if(linksEditor==null)
            {
                bool dirty=false;
                linksEditor=new LinksWindow(themes,delegate{dirty=true;SetNotice("Источники сохранены. После закрытия окна каталог обновится.");}){Owner=this};
                System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(linksEditor);
                linksEditor.Closed+=delegate{linksEditor=null;if(dirty&&!exiting)Run(ReloadSources);};
            }
            SettingsPlacement.Place(this,linksEditor);linksEditor.Show();linksEditor.Activate();
        }
        private async Task ReloadSources()
        {
            SetNotice("Обновление пользовательских источников…");await manager.ReloadSourcesAsync();if(exiting)return;Rebuild();
            var errors=manager.SourceErrors;SetNotice(errors.Count==0?"Источники и каталог обновлены":"Источники обновлены. Недоступных источников: "+errors.Count+". "+errors[0]);
        }
        internal LinksWindow LinksEditor{get{return linksEditor;}}
        internal MixerLayoutWindow MixerEditor{get{return mixerEditor;}}
        internal void OpenStyles()
        {
            if(editor==null) { editor=new StyleWindow(themes); editor.Owner=this;System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(editor); editor.Closed+=delegate { editor=null; }; }
            SettingsPlacement.Place(this,editor);editor.Show(); editor.Activate();
        }
        internal StyleWindow StyleEditor { get { return editor; } }
        internal void OpenControls()
        {
            if(controlsEditor==null){controlsEditor=new ControlsWindow(themes,controls);controlsEditor.Owner=this;System.Windows.Forms.Integration.ElementHost.EnableModelessKeyboardInterop(controlsEditor);controlsEditor.Closed+=delegate{controlsEditor=null;};}
            SettingsPlacement.Place(this,controlsEditor);controlsEditor.Show();controlsEditor.Activate();
        }
        internal ControlsWindow ControlsEditor {get{return controlsEditor;}}
        private void ControlsChanged(object sender,EventArgs e){if(!exiting&&!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{if(!exiting)RefreshControls();}));}
        private void RefreshControls(){HotkeyRuntimeStatus state=controls.GetRuntime("hub.toggle");string key=state==null?controls.Current.Get("hub.toggle"):state.Actual;shortcut.Text=(string.IsNullOrEmpty(key)?"Показ через трей":key)+"\nКрестик скрывает HUB";}
        internal void ShowHub() { Show(); WindowState=WindowState.Normal; Native.SetForegroundWindow(new WindowInteropHelper(this).Handle); Activate(); }
        internal void HideHub() { if(editor!=null) editor.Hide();if(controlsEditor!=null)controlsEditor.Hide();if(behaviorEditor!=null)behaviorEditor.Hide();if(linksEditor!=null)linksEditor.Hide();if(mixerEditor!=null)mixerEditor.Hide(); Hide(); }
        internal void Exit() { exiting=true; if(editor!=null) editor.Close();if(controlsEditor!=null)controlsEditor.Close();if(behaviorEditor!=null)behaviorEditor.Close();if(linksEditor!=null)linksEditor.Close();if(mixerEditor!=null)mixerEditor.Close(); Close(); }
        internal bool StatusTimerRunning { get { return statusTimer.IsEnabled; } }
    }

    internal sealed class ModuleCard : Border,IDisposable
    {
        internal readonly ModuleView View;
        private readonly ModuleManager manager;
        private readonly ControlsStore controls;
        private readonly Action<string> notify;
        private readonly TextBlock state,details,version;
        private readonly Button primary,open,update,remove,folder;
        private readonly TextBox installationPath;
        private readonly ModulePreview previewWindow;
        internal ModuleCard(ModuleView module,ModuleManager service,Action<string> notice,ControlsStore bindings,string themeRoot=null)
        {
            View=module; manager=service; notify=notice;
            controls=bindings;
            Padding=new Thickness(16); Margin=new Thickness(0,0,0,12); BorderThickness=new Thickness(1); CornerRadius=new CornerRadius(10);
            SetResourceReference(BackgroundProperty,"Hub.Surface"); SetResourceReference(BorderBrushProperty,"Hub.Border");
            Grid layout=new Grid(); layout.ColumnDefinitions.Add(new ColumnDefinition()); layout.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(236) }); Child=layout;
            StackPanel text=new StackPanel { Margin=new Thickness(0,0,16,0) }; layout.Children.Add(text);
            DockPanel heading=new DockPanel(); state=Ui.Text("NO",12,"Hub.Muted"); state.FontWeight=FontWeights.Bold; state.Margin=new Thickness(12,3,0,0); DockPanel.SetDock(state,Dock.Right); heading.Children.Add(state); heading.Children.Add(Ui.Text(module.Manifest.Name,18,"Hub.Foreground")); text.Children.Add(heading);
            version=Ui.Text("",12,"Hub.Foreground"); version.Margin=new Thickness(0,4,0,9); text.Children.Add(version);
            text.Children.Add(Ui.Text(module.Manifest.Description,12,"Hub.Muted"));
            details=Ui.Text("",11,"Hub.Muted"); details.Margin=new Thickness(0,7,0,0); text.Children.Add(details);
            installationPath=Ui.FolderPath("");System.Windows.Automation.AutomationProperties.SetName(installationPath,"Папка утилиты: "+module.Manifest.Name);text.Children.Add(installationPath);
            WrapPanel actions=new WrapPanel { Margin=new Thickness(0,12,0,0) }; text.Children.Add(actions);
            primary=Ui.Button("Установить",delegate { InvokePrimary(); },true); actions.Children.Add(primary);
            open=Ui.Button("Открыть",delegate { Run(delegate { return manager.ShowAsync(View); }); },false); open.Margin=new Thickness(7,0,0,0); actions.Children.Add(open);
            update=Ui.Button("Обновить",delegate { Run(async delegate { notify("Обновление «"+View.Manifest.Name+"»: загрузка и проверка пакета…");await manager.UpdateAsync(View);if(View.State=="ON"&&View.Manifest.Kind=="process")await manager.ShowAsync(View); }); },false); update.Margin=new Thickness(7,0,0,0); actions.Children.Add(update);
            remove=Ui.Button("Удалить утилиту",Uninstall,false);remove.Margin=new Thickness(7,0,0,0);remove.ToolTip="Остановить утилиту и удалить её файлы. Личные данные сохраняются.";actions.Children.Add(remove);
            folder=Ui.Button("Открыть папку",delegate{try{Ui.OpenFolder(View.InstallDirectory);}catch(Exception ex){notify(ex.Message);}},false);folder.Margin=new Thickness(7,0,0,0);folder.ToolTip="Открыть папку установленной версии утилиты";actions.Children.Add(folder);
            StackPanel preview=new StackPanel { VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Right };
            previewWindow=new ModulePreview(module.Manifest,themeRoot);preview.Children.Add(previewWindow); TextBlock caption=Ui.Text("Предпросмотр оформления",10,"Hub.Muted"); caption.HorizontalAlignment=HorizontalAlignment.Center; caption.Margin=new Thickness(0,6,0,0); preview.Children.Add(caption); Grid.SetColumn(preview,1); layout.Children.Add(preview);
            View.PropertyChanged+=Changed; Update();
        }
        private void Changed(object sender,PropertyChangedEventArgs e) { if(!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(Update)); }
        internal void Update()
        {
            state.Text=View.State; state.SetResourceReference(TextBlock.ForegroundProperty,View.State=="ON"?"Hub.Accent":"Hub.Muted");
            state.ToolTip=View.State=="NO"?"Не установлено":View.State=="ON"?"Состояние проверено: работает":"Установлено, выключено";
            version.Inlines.Clear();version.Inlines.Add(new System.Windows.Documents.Run(string.IsNullOrEmpty(View.InstalledVersion)?"Не установлена · ":"Установлена: v"+View.InstalledVersion));if(string.IsNullOrEmpty(View.InstalledVersion)||new Version(View.Manifest.Version)>new Version(View.InstalledVersion)){var available=new System.Windows.Documents.Run((string.IsNullOrEmpty(View.InstalledVersion)?"":" · ")+"доступна v"+View.Manifest.Version){FontSize=14,FontWeight=FontWeights.Bold};available.SetResourceReference(System.Windows.Documents.TextElement.ForegroundProperty,"Hub.Tertiary");version.Inlines.Add(available);}
            details.Text=View.Detail??"";
            installationPath.Text=View.InstallDirectory;installationPath.ToolTip=View.InstallDirectory;installationPath.Visibility=string.IsNullOrEmpty(View.InstallDirectory)?Visibility.Collapsed:Visibility.Visible;
            folder.Visibility=installationPath.Visibility;folder.IsEnabled=!View.IsBusy;
            primary.Content=View.IsBusy?"Подождите…":View.State=="NO"?"Установить":View.State=="ON"?"Выключить":View.Manifest.Kind=="fix"?"Применить":"Включить";
            primary.IsEnabled=!View.IsBusy; open.IsEnabled=!View.IsBusy;
            open.Visibility=View.State=="ON" && View.Manifest.Kind!="fix"?Visibility.Visible:Visibility.Collapsed;
            update.Visibility=!string.IsNullOrEmpty(View.InstalledVersion) && View.InstalledVersion!=View.Manifest.Version?Visibility.Visible:Visibility.Collapsed; update.IsEnabled=!View.IsBusy;
            update.ToolTip=View.State=="ON"?"Скачать и проверить обновление, затем перезапустить и открыть утилиту с сохранением данных.":"Установить новую версию с сохранением личных данных";System.Windows.Controls.ToolTipService.SetShowOnDisabled(update,true);
            remove.Visibility=string.IsNullOrEmpty(View.InstalledVersion)?Visibility.Collapsed:Visibility.Visible;remove.IsEnabled=!View.IsBusy;
        }
        private void Uninstall()
        {
            Window owner=Window.GetWindow(this);
            string message="Удалить «"+View.Manifest.Name+"»? Работающая утилита будет остановлена. Личные данные и оформление сохранятся.";
            MessageBoxResult decision=owner==null?MessageBox.Show(message,"Удаление утилиты",MessageBoxButton.YesNo,MessageBoxImage.Question):MessageBox.Show(owner,message,"Удаление утилиты",MessageBoxButton.YesNo,MessageBoxImage.Question);
            if(decision!=MessageBoxResult.Yes)return;
            Run(async delegate { await manager.UninstallAsync(View);if(View.Manifest.Id=="quiet-mixer"||View.Manifest.Id=="task-menu"){var p=controls.Current;if(View.Manifest.Id=="quiet-mixer")p.StartMixerWithHub=false;else p.StartTaskMenuWithHub=false;controls.Save(p);} });
        }
        private void InvokePrimary(){Run(async delegate
        {
            if(View.State=="NO"){await manager.InstallAsync(View);return;}
            bool enable=View.State!="ON";if(enable)await manager.StartAsync(View);else await manager.StopAsync(View);
            if(View.Manifest.Id=="quiet-mixer"||View.Manifest.Id=="task-menu"){ControlsProfile profile=controls.Current;if(View.Manifest.Id=="quiet-mixer")profile.StartMixerWithHub=enable;else profile.StartTaskMenuWithHub=enable;controls.Save(profile);}
            if(enable&&View.Manifest.Kind=="process")await manager.ShowAsync(View);
        });}
        internal void ApplyPreview(ThemeProfile profile){previewWindow.ApplyTheme(profile);}
        private async void Run(Func<Task> action) { try { await action(); notify("Готово: "+View.Manifest.Name); } catch(Exception ex) { notify(ex.Message); } finally { Update(); } }
        public void Dispose() { View.PropertyChanged-=Changed;previewWindow.Dispose(); }
    }
}
