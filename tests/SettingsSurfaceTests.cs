using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReFix.Shared;
using ReFix.Hub;
namespace ReFix.Tests
{
    internal static class SettingsSurfaceTests
    {
        internal static void Run(Action<string,bool> report)
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"settings-surface-"+Guid.NewGuid().ToString("N"));
            try
            {
                using(var themes=new ThemeStore(root))using(var controls=new ControlsStore(root))
                {
                    Check(new SettingsWindow(themes),"settings selector draws opaque background",report);
                    Check(new BehaviorWindow(themes,controls,delegate{return false;},delegate{}),"behavior editor draws its own background",report);
                    var custom=themes.Current;custom.Mode="Custom";custom.BackgroundOpacity=0;custom.Background="#233447";themes.Save(custom);
                    Check(new SettingsWindow(themes),"settings stays readable with fully transparent HUB theme",report);
                    report("settings opacity safety does not change user theme",themes.Current.BackgroundOpacity==0);
                    CheckIconPicker(themes,report);
                }
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        static void CheckIconPicker(ThemeStore themes,Action<string,bool> report)
        {
            var module=new ModuleView{Manifest=new ModuleManifest{Id="future-extension",Name="Новое расширение"}};
            var window=new IconPickerWindow(themes,new[]{module});
            try
            {
                window.Show();window.UpdateLayout();
                var target=Children<ComboBox>(window).Single();
                target.SelectedIndex=1;
                var folder=Children<Button>(window).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Папка");
                folder.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                report("the icon picker applies a Windows icon to an arbitrary catalogue extension",
                    themes.Current.WindowsIconFor("future-extension")==3&&!themes.Current.WindowsIconFor("hub").HasValue);
                var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);
                bitmap.Render((Visual)window.Content);
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
                using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"windows-icon-picker.png")))png.Save(file);
                Children<Button>(window).Single(b=>Equals(b.Content,"По умолчанию")).RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                report("gallery reset restores only the selected extension icon",!themes.Current.WindowsIconFor("future-extension").HasValue);
            }
            finally{window.Close();}
        }
        static IEnumerable<T> Children<T>(DependencyObject parent) where T:DependencyObject
        {
            var match=parent as T;
            if(match!=null)yield return match;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++)
                foreach(var child in Children<T>(VisualTreeHelper.GetChild(parent,i)))yield return child;
        }
        static void Check(Window window,string name,Action<string,bool> report)
        {
            try
            {
                window.Show();window.UpdateLayout();var frame=(Border)window.Content;
                var bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render(frame);
                byte[] pixel=new byte[4];bitmap.CopyPixels(new Int32Rect((int)window.ActualWidth-44,(int)window.ActualHeight-44,1,1),pixel,4,0);
                report(name,frame.Background!=null&&frame.Background.Opacity==1&&pixel[3]==255);
            }
            finally{window.Close();}
        }
    }
}
