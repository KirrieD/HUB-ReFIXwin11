using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReFix.Shared;
using ReFix.Hub;

namespace ReFix.Tests
{
    internal static class UpdateWindowTests
    {
        static T Find<T>(DependencyObject root,Func<T,bool> match)where T:DependencyObject
        {
            var value=root as T;if(value!=null&&match(value))return value;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=Find<T>(VisualTreeHelper.GetChild(root,i),match);if(found!=null)return found;}return null;
        }
        static void Pump(){var end=DateTime.UtcNow.AddMilliseconds(180);while(DateTime.UtcNow<end){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);System.Threading.Thread.Sleep(5);}}
        internal static void Run(Action<string,bool> report)
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-ui-"+Guid.NewGuid().ToString("N"));
            try
            {
                using(var themes=new ThemeStore(root))
                {
                    var release=new HubRelease{Version="2.0.0",Notes="Новый выпуск HUB.\n"+new string('Д',2000)};
                    var confirm=new UpdateWindow(themes,UpdateWindowKind.Confirm,release);confirm.Show();Pump();
                    report("update dialog uses HUB frame and scrollable release notes",confirm.WindowStyle==WindowStyle.None&&Find<ScrollViewer>(confirm,x=>true)!=null&&Find<Button>(confirm,b=>Convert.ToString(b.Content)=="Обновить")!=null);
                    var p=themes.Current;p.Mode="Custom";p.Background="#202126";p.Accent="#E60000";p.BackgroundOpacity=0;themes.Save(p);Pump();
                    report("open update dialog follows live theme with readable surface",((SolidColorBrush)confirm.Resources["Hub.Background"]).Color==Color.FromRgb(32,33,38)&&((Border)confirm.Content).Background.Opacity==1);
                    var bitmap=new RenderTargetBitmap((int)confirm.ActualWidth,(int)confirm.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render((Visual)confirm.Content);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-confirm.png")))png.Save(file);
                    confirm.Close();report("closing confirmation declines update",!confirm.Accepted);
                    var accept=new UpdateWindow(themes,UpdateWindowKind.Confirm,release);Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{Find<Button>(accept,b=>Convert.ToString(b.Content)=="Обновить").RaiseEvent(new RoutedEventArgs(Button.ClickEvent));}));accept.ShowDialog();report("accept button confirms modal update",accept.Accepted);
                    var progress=new UpdateWindow(themes,UpdateWindowKind.Progress);progress.Show();progress.SetProgress(42,"Проверка файла…");Pump();progress.Close();
                    report("progress remains open until coordinator finishes and displays percentage",progress.IsVisible&&Find<ProgressBar>(progress,x=>true).Value==42&&!Find<ProgressBar>(progress,x=>true).IsIndeterminate);
                    progress.SetProgress(-1,"Подготовка…");report("unknown progress switches back to indeterminate",Find<ProgressBar>(progress,x=>true).IsIndeterminate);progress.Finish();report("coordinator can close locked progress",!progress.IsVisible);
                    var failure=new UpdateWindow(themes,UpdateWindowKind.Failure,null,"Ошибка соединения");failure.Show();Pump();report("update failure uses same themed window",failure.WindowStyle==WindowStyle.None&&Find<Button>(failure,b=>Convert.ToString(b.Content)=="Понятно")!=null);failure.Close();
                }
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
