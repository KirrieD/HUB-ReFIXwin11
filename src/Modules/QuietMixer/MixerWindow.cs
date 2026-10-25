using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using ReFix.Shared;
using ReFix.Modules;

namespace QuietMixer
{
    internal sealed class MixerWindow : Window
    {
        private readonly StackPanel rows = new StackPanel();
        private readonly TextBlock status = new TextBlock();
        private readonly DispatcherTimer timer;
        private readonly Dictionary<string, ChannelRow> views = new Dictionary<string, ChannelRow>();
        private AudioEngine audio;
        private readonly List<AudioChannel> channels = new List<AudioChannel>();
        private ThemedSurface surface;
        private ThemeProfile currentTheme;
        private bool dark;
        private bool positioning;
        private bool closing;
        internal bool RefreshRunning { get { return timer.IsEnabled; } }
        internal bool HasAudio { get { return audio != null; } }
        internal int RowCount { get { return views.Count; } }
        readonly ControlsStore controls; readonly bool ownsControls; EventHandler controlsChanged;
        internal MixerWindow(ThemeProfile profile) : this(profile,null) {}
        internal MixerWindow(ThemeProfile profile,ControlsStore controls)
        {
            this.controls=controls??new ControlsStore(Paths.Root);ownsControls=controls==null;controlsChanged=delegate{Dispatcher.BeginInvoke(new Action(UpdateControlsHelp));};this.controls.Changed+=controlsChanged;
            Title = "QuietMixer v"+ProductInfo.Version+" — микшер громкости"; Width = 420; Height = 520;
            WindowStyle = WindowStyle.None; AllowsTransparency=true; ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false; Topmost = true; WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"); FontSize = 13;
            ApplyTheme(profile);
            BuildStyles();
            SizeChanged+=delegate { ApplyTheme(currentTheme); };
            Grid root = new Grid { Margin = new Thickness(20,16,20,16) };

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1,GridUnitType.Star) });

            root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid header = new Grid { Margin = new Thickness(0,0,0,18) };
            header.ColumnDefinitions.Add(new ColumnDefinition()); header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            StackPanel title = new StackPanel();
            title.Children.Add(new TextBlock { Text="Микшер громкости", FontSize=21, FontWeight=FontWeights.SemiBold });
            title.Children.Add(new TextBlock { Text="v"+ProductInfo.Version+" · Звук под вашим контролем", FontSize=12, Opacity=.6, Margin=new Thickness(0,5,0,0) });
            header.Children.Add(title);
            Button menu = new Button { Content="⋯", Width=36, Height=34, FontSize=22, ToolTip="Настройки QuietMixer" };
            AutomationProperties.SetName(menu,"Настройки QuietMixer"); Grid.SetColumn(menu,1);
            menu.Click += delegate { ContextMenu c = BuildMenu(); menu.ContextMenu=c; c.PlacementTarget=menu; c.IsOpen=true; };
            header.Children.Add(menu); root.Children.Add(header);
            ScrollViewer scroll = new ScrollViewer { Content=rows, VerticalScrollBarVisibility=ScrollBarVisibility.Auto, HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled, Padding=new Thickness(0,0,5,0) };
            Grid.SetRow(scroll,1); root.Children.Add(scroll);
            UpdateControlsHelp(); status.FontSize=11; status.Opacity=.6; status.Margin=new Thickness(0,14,0,0); status.TextWrapping=TextWrapping.Wrap;
            Grid.SetRow(status,2); root.Children.Add(status);
            surface=new ThemedSurface(root); Content=surface; surface.Apply(this,currentTheme);
            timer = new DispatcherTimer { Interval=TimeSpan.FromSeconds(1) };
            timer.Tick += delegate { RefreshAudio(); };
            IsVisibleChanged += delegate { if (IsVisible) { RefreshAudio(); timer.Start(); } else { timer.Stop(); ReleaseAudio(); } };
            Deactivated += delegate { if (!closing && !positioning && !ContextMenuOpen()) Hide(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (KeyChord.Parse(this.controls.Current.Get("quiet-mixer.hide")).Matches(e.Key==Key.System?e.SystemKey:e.Key,Keyboard.Modifiers)) { Hide(); e.Handled=true; } };
            SourceInitialized += delegate { ApplyCorners(); };
            Closed += delegate { closing=true;this.controls.Changed-=controlsChanged;if(ownsControls)this.controls.Dispose();timer.Stop(); ReleaseAudio(); };
        }
        private void UpdateControlsHelp() { if(closing)return;var p=controls.Current;string toggle=p.Get("quiet-mixer.toggle"),hide=p.Get("quiet-mixer.hide");status.Text="Устройство по умолчанию"+(toggle!=""?" · "+toggle:"")+(hide!=""?" · "+hide+" — скрыть":""); }
        private bool ContextMenuOpen()
        {
            return FindOpenMenu(this);
        }
        private static bool FindOpenMenu(DependencyObject element)
        {
            FrameworkElement f = element as FrameworkElement;
            if (f != null && f.ContextMenu != null && f.ContextMenu.IsOpen) return true;
            int count=VisualTreeHelper.GetChildrenCount(element);
            for (int i=0;i<count;i++) if (FindOpenMenu(VisualTreeHelper.GetChild(element,i))) return true;
            return false;
        }
        internal void ApplyTheme(ThemeProfile profile)
        {
            currentTheme=profile;
            ThemeProfile resolved=ThemeProfile.Resolve(profile);
            dark=ThemeApplier.ColorBrush(resolved.Background).Color.R<128;
            if(surface!=null) surface.Apply(this,resolved); else { ThemeApplier.Apply(this,resolved); Background=Brushes.Transparent; }
            if(IsLoaded) ApplyCorners();
        }
        private void BuildStyles()
        {
            string xaml=@"<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
<Style TargetType='Button'><Setter Property='Background' Value='Transparent'/><Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/><Setter Property='BorderThickness' Value='0'/><Setter Property='Padding' Value='8,4'/><Setter Property='Cursor' Value='Hand'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'><Border x:Name='b' CornerRadius='6' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'><ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource Hub.Surface}'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='b' Property='BorderThickness' Value='1'/><Setter TargetName='b' Property='BorderBrush' Value='{DynamicResource Hub.Accent}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='Slider'><Setter Property='Height' Value='28'/><Setter Property='Minimum' Value='0'/><Setter Property='Maximum' Value='100'/><Setter Property='SmallChange' Value='1'/><Setter Property='LargeChange' Value='5'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'><Grid Background='Transparent' Margin='2,0'>
<Track x:Name='PART_Track'><Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge'><RepeatButton.Template><ControlTemplate><Border Height='4' CornerRadius='2' Background='{DynamicResource Hub.Accent}'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
<Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge'><RepeatButton.Template><ControlTemplate><Border Height='4' CornerRadius='2' Background='{DynamicResource Hub.Border}'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
<Track.Thumb><Thumb Width='20' Height='20'><Thumb.Template><ControlTemplate><Grid><Ellipse Fill='{DynamicResource Hub.Surface}' Stroke='#808080' StrokeThickness='0.5'/><Ellipse x:Name='dot' Fill='{DynamicResource Hub.Accent}' Margin='5'/></Grid><ControlTemplate.Triggers><Trigger Property='IsMouseOver' Value='True'><Setter TargetName='dot' Property='Margin' Value='4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Thumb.Template></Thumb></Track.Thumb></Track>
</Grid><ControlTemplate.Triggers><Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='Opacity' Value='0.75'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>
<Style TargetType='ScrollBar'>
  <Setter Property='Width' Value='9'/><Setter Property='Background' Value='Transparent'/>
  <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
    <Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='True'>
      <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand'><RepeatButton.Template><ControlTemplate><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
      <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand'><RepeatButton.Template><ControlTemplate><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
      <Track.Thumb><Thumb><Thumb.Template><ControlTemplate><Border CornerRadius='3' Margin='2,0' Background='{DynamicResource Hub.Border}'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
    </Track></Grid>
  </ControlTemplate></Setter.Value></Setter>
</Style>
</ResourceDictionary>";
            Resources.MergedDictionaries.Clear(); Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(xaml));
        }

        private void ApplyCorners()
        {
            IntPtr hwnd=new WindowInteropHelper(this).Handle;
            int corner=2, theme=dark?1:0; Native.DwmSetWindowAttribute(hwnd,33,ref corner,4); Native.DwmSetWindowAttribute(hwnd,20,ref theme,4);
        }
        internal void ShowAtCursor()
        {
            positioning=true;
            Native.Point cursor; Native.GetCursorPos(out cursor);
            Rect work=Native.WorkArea(cursor);
            // Place in physical pixels, then let WPF convert at the target monitor DPI.
            IntPtr handle=new WindowInteropHelper(this).EnsureHandle();
            Native.SetWindowPos(handle,IntPtr.Zero,cursor.X,cursor.Y,0,0,0x15);
            HwndSource source=HwndSource.FromHwnd(handle);
            Matrix from=source.CompositionTarget.TransformFromDevice;
            Height=Math.Min(520, Math.Max(220,work.Height*from.M22-24)); Width=Math.Min(420,Math.Max(280,work.Width*from.M11-24));
            Matrix to=source.CompositionTarget.TransformToDevice;
            Point position=Policy.Place(work,Width*to.M11,Height*to.M22,new Point(cursor.X,cursor.Y));
            Show(); Native.SetWindowPos(handle,IntPtr.Zero,(int)position.X,(int)position.Y,0,0,0x15);
            ApplyCorners(); Native.SetForegroundWindow(handle); Activate();
            Dispatcher.BeginInvoke(new Action(delegate { positioning=false; }), DispatcherPriority.ApplicationIdle);
        }
        private ContextMenu BuildMenu()
        {
            ContextMenu c=new ContextMenu { Resources=Resources };
            c.SetResourceReference(Control.BackgroundProperty,"Hub.Surface"); c.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");
            MenuItem settings=new MenuItem { Header="Параметры звука Windows" };
            settings.Click+=delegate { System.Diagnostics.Process.Start("ms-settings:sound"); Hide(); }; c.Items.Add(settings);
            c.Items.Add(new Separator());
            MenuItem exit=new MenuItem { Header="Выйти из QuietMixer" }; exit.Click+=delegate { Application.Current.Shutdown(); }; c.Items.Add(exit);
            c.Closed+=delegate { if (!IsActive) Hide(); }; return c;
        }
        private void ReleaseAudio()
        {
            foreach(ChannelRow view in views.Values) view.Deactivate();
            rows.Children.Clear(); views.Clear();
            foreach (AudioChannel channel in channels) channel.Dispose(); channels.Clear();
            if (audio != null) { audio.Dispose(); audio=null; }
        }
        private void RefreshAudio()
        {
            if (!IsVisible) return;
            try
            {
                if (audio != null && !audio.IsCurrentDevice()) ReleaseAudio();
                if (audio==null) audio=new AudioEngine();
                if (!views.ContainsKey("master"))
                {
                    rows.Children.Clear();
                    ChannelRow master=new ChannelRow(audio.Master,dark,true); views.Add("master",master); rows.Children.Add(master);
                    rows.Children.Add(new TextBlock { Text="ПРИЛОЖЕНИЯ", FontSize=10, FontWeight=FontWeights.SemiBold, Opacity=.6, Margin=new Thickness(2,16,0,10) });
                }
                List<AudioChannel> current;
                try { current=audio.Sessions(); }
                catch(UnauthorizedAccessException)
                {
                    views["master"].Sync();
                    status.Text="Windows запретила доступ к громкости приложений. Общая громкость доступна. Запустите QuietMixer обычным способом вне ограниченной среды.";
                    return;
                }
                HashSet<string> ids=new HashSet<string>(current.Select(delegate(AudioChannel ch) { return ch.Id; }));
                foreach (string id in views.Keys.Where(delegate(string id) { return id!="master" && !ids.Contains(id); }).ToArray())
                {
                    ChannelRow view=views[id]; view.Deactivate(); rows.Children.Remove(view); channels.Remove(view.Channel); view.Channel.Dispose(); views.Remove(id);
                }
                foreach (AudioChannel channel in current)
                {
                    if (views.ContainsKey(channel.Id)) { channel.Dispose(); continue; }
                    ChannelRow row=new ChannelRow(channel,dark,false); views.Add(channel.Id,row); channels.Add(channel); rows.Children.Add(row);
                }
                foreach (ChannelRow view in views.Values) view.Sync();
                UpdateControlsHelp();
            }
            catch (COMException ex)
            {
                ReleaseAudio(); rows.Children.Add(new TextBlock { Text="Нет доступного устройства вывода", FontSize=16, Margin=new Thickness(2,20,2,12), TextWrapping=TextWrapping.Wrap });
                status.Text="Подключите наушники или динамики. Микшер проверит устройство снова автоматически."; Diagnostics.Log(ex);
            }
            catch (UnauthorizedAccessException ex)
            {
                ReleaseAudio(); rows.Children.Add(new TextBlock { Text="Доступ к звуку ограничен Windows", FontSize=16, TextWrapping=TextWrapping.Wrap });
                status.Text="Запустите QuietMixer обычным способом из Проводника."; Diagnostics.Log(ex);
            }
        }

        private sealed class ChannelRow : Border
        {
            internal readonly AudioChannel Channel;
            private readonly Slider slider;
            private readonly TextBlock value;
            private readonly Button mute;
            private bool syncing;
            private bool active=true;
            internal void Deactivate() { active=false; }
            internal ChannelRow(AudioChannel channel,bool dark,bool master)
            {
                Channel=channel; SetResourceReference(Border.CornerRadiusProperty,"Hub.Corner"); SetResourceReference(Border.BackgroundProperty,"Hub.Surface");
                SetResourceReference(Border.BorderBrushProperty,"Hub.Border"); BorderThickness=new Thickness(1); Padding=new Thickness(12,10,12,8); Margin=new Thickness(0,0,0,7);
                Grid g=new Grid(); g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(38) }); g.ColumnDefinitions.Add(new ColumnDefinition()); g.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(38) });
                g.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto }); g.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
                mute=new Button { Content="\uE767", FontFamily=new FontFamily("Segoe MDL2 Assets"), FontSize=18, Width=32, Height=32, Padding=new Thickness(0), ToolTip="Выключить / включить звук" };
                AutomationProperties.SetName(mute,"Выключить / включить звук: "+channel.Name); Grid.SetRowSpan(mute,2);
                mute.Click+=delegate { Try(delegate { Channel.Muted=!Channel.Muted; }); }; g.Children.Add(mute);
                TextBlock name=new TextBlock { Text=channel.Name, FontWeight=master?FontWeights.SemiBold:FontWeights.Normal, TextTrimming=TextTrimming.CharacterEllipsis, VerticalAlignment=VerticalAlignment.Center, ToolTip=channel.Name };
                Grid.SetColumn(name,1); g.Children.Add(name);
                value=new TextBlock { TextAlignment=TextAlignment.Right, FontSize=12, Opacity=.65 }; Grid.SetColumn(value,2); g.Children.Add(value);
                slider=new Slider { Minimum=0, Maximum=100, IsMoveToPointEnabled=true, Margin=new Thickness(0,4,0,0) };
                AutomationProperties.SetName(slider,"Громкость: "+channel.Name);
                Grid.SetColumn(slider,1); Grid.SetRow(slider,1); Grid.SetColumnSpan(slider,2);
                slider.ValueChanged+=delegate { if(!syncing) Try(delegate { Channel.Volume=Policy.Volume(slider.Value); }); };
                slider.PreviewMouseWheel+=delegate(object s,MouseWheelEventArgs e) { slider.Value=Math.Max(0,Math.Min(100,slider.Value+(e.Delta>0?2:-2))); e.Handled=true; };
                g.Children.Add(slider); Child=g; Sync();
            }
            private void Try(Action operation) { if(!active) return; try { operation(); Sync(); } catch(COMException) { IsEnabled=false; } catch(UnauthorizedAccessException) { IsEnabled=false; } }
            internal void Sync()
            {
                if(!active) return;
                try
                {
                    syncing=true;
                    if (!slider.IsMouseCaptureWithin && !slider.IsKeyboardFocusWithin) slider.Value=Math.Round(Channel.Volume*100);
                    value.Text=Math.Round(slider.Value).ToString();
                    bool muted=Channel.Muted; mute.Content=muted?"\uE74F":"\uE767";
                    AutomationProperties.SetHelpText(mute,muted?"Звук выключен":"Звук включён");
                }
                catch(COMException) { IsEnabled=false; }
                catch(UnauthorizedAccessException) { IsEnabled=false; }
                finally { syncing=false; }
            }
        }
    }
}
