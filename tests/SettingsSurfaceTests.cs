using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
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
                }
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
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
