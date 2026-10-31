using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Forms.Integration;
using ReFix.Shared;

namespace ReFix.Hub
{
    internal enum UpdateWindowKind { Confirm, Progress, Failure }

    internal sealed class UpdateWindow:Window
    {
        readonly ThemeStore themes;
        readonly Border frame,image;
        readonly TextBlock phase;
        readonly ProgressBar progress;
        readonly bool locked;
        bool closed,allowClose;
        internal bool Accepted {get;private set;}
        internal UpdateWindow(ThemeStore themes,UpdateWindowKind kind,HubRelease release=null,string failure=null)
        {
            this.themes=themes;locked=kind==UpdateWindowKind.Progress;
            Title="Обновление HUB ReFIX";Width=600;Height=kind==UpdateWindowKind.Confirm?430:280;MinWidth=440;MinHeight=240;
            WindowStartupLocation=WindowStartupLocation.CenterScreen;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;ResizeMode=locked?ResizeMode.NoResize:ResizeMode.CanResizeWithGrip;
            Ui.Styles(this);frame=new Border{Margin=new Thickness(18),BorderThickness=new Thickness(1)};Content=frame;
            var layers=new Grid();frame.Child=layers;image=new Border{IsHitTestVisible=false};layers.Children.Add(image);
            var root=new DockPanel{Margin=new Thickness(24)};layers.Children.Add(root);
            var header=new DockPanel{Margin=new Thickness(0,0,0,20),Background=Brushes.Transparent};
            if(!locked){var close=Ui.Button("✕",Close,false);DockPanel.SetDock(close,Dock.Right);header.Children.Add(close);}
            header.Children.Add(Ui.Text("Обновление HUB",23,"Hub.Foreground"));DockPanel.SetDock(header,Dock.Top);root.Children.Add(header);
            header.MouseLeftButtonDown+=delegate(object sender,MouseButtonEventArgs e){if(e.OriginalSource is TextBlock||e.OriginalSource==header)try{DragMove();}catch(InvalidOperationException){}};
            var actions=new StackPanel{Orientation=Orientation.Horizontal,HorizontalAlignment=HorizontalAlignment.Right,Margin=new Thickness(0,20,0,0)};DockPanel.SetDock(actions,Dock.Bottom);root.Children.Add(actions);
            var body=new StackPanel();root.Children.Add(new ScrollViewer{Content=body,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
            if(kind==UpdateWindowKind.Confirm)
            {
                var title=Ui.Text("Доступна новая версия "+release.Version,19,"Hub.Tertiary");title.FontWeight=FontWeights.SemiBold;body.Children.Add(title);
                var current=Ui.Text("Сейчас установлена "+ProductInfo.Version+". После установки HUB перезапустится.",13,"Hub.Muted");current.Margin=new Thickness(0,12,0,18);body.Children.Add(current);
                body.Children.Add(Ui.Text(release.Notes??"",14,"Hub.Foreground"));
                var cancel=Ui.Button("Отмена",Close,false);cancel.Margin=new Thickness(0,0,10,0);actions.Children.Add(cancel);
                var accept=Ui.Button("Обновить",delegate{Accepted=true;Close();},true);accept.IsDefault=true;actions.Children.Add(accept);
            }
            else if(locked)
            {
                phase=Ui.Text("Скачивание и проверка обновления…",14,"Hub.Foreground");body.Children.Add(phase);
                progress=new ProgressBar{Minimum=0,Maximum=100,Height=7,Margin=new Thickness(0,20,0,0),IsIndeterminate=true};progress.SetResourceReference(Control.ForegroundProperty,"Hub.Accent");progress.SetResourceReference(Control.BackgroundProperty,"Hub.Surface");body.Children.Add(progress);
                var hint=Ui.Text("После установки HUB перезапустится автоматически.",12,"Hub.Muted");hint.Margin=new Thickness(0,14,0,0);body.Children.Add(hint);
            }
            else
            {
                body.Children.Add(Ui.Text("Обновление не установлено",18,"Hub.Tertiary"));
                var text=Ui.Text("Текущая версия сохранена.\n\n"+failure,13,"Hub.Foreground");text.Margin=new Thickness(0,12,0,0);body.Children.Add(text);actions.Children.Add(Ui.Button("Понятно",Close,true));
            }
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(!locked&&e.Key==Key.Escape){Close();e.Handled=true;}};
            Closing+=delegate(object sender,CancelEventArgs e){if(locked&&!allowClose)e.Cancel=true;};
            Closed+=delegate{closed=true;themes.Changed-=Changed;};themes.Changed+=Changed;SizeChanged+=delegate{Apply();};Apply();ElementHost.EnableModelessKeyboardInterop(this);
        }
        void Apply(){if(!closed)Ui.Apply(this,themes.Current,frame,image,true);}
        void Changed(object sender,EventArgs e){if(!closed&&!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(Apply));}
        internal void SetProgress(int percent,string text){if(closed||progress==null)return;phase.Text=text;progress.IsIndeterminate=percent<0;if(percent>=0)progress.Value=Math.Max(0,Math.Min(100,percent));}
        internal void Finish(){allowClose=true;Close();}
    }

    internal sealed class UpdatePresentation:IUpdatePresentation
    {
        readonly ThemeStore themes;UpdateWindow progress;
        internal UpdatePresentation(ThemeStore themes){this.themes=themes;}
        public bool Confirm(HubRelease release){var dialog=new UpdateWindow(themes,UpdateWindowKind.Confirm,release);dialog.ShowDialog();return dialog.Accepted;}
        public void ShowProgress(){CloseProgress();progress=new UpdateWindow(themes,UpdateWindowKind.Progress);progress.Show();}
        public void SetProgress(int percent,string text){if(progress!=null)progress.SetProgress(percent,text);}
        public void CloseProgress(){if(progress!=null){progress.Finish();progress=null;}}
        public void ShowFailure(string message){new UpdateWindow(themes,UpdateWindowKind.Failure,null,message).ShowDialog();}
        public void Dispose(){CloseProgress();}
    }
}
