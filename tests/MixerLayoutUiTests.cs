#if MIXER_LAYOUT_UI_FIXTURE
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReFix.Shared;
using QuietMixer;
namespace ReFix.Tests
{
    internal static class MixerLayoutUiTests
    {
        sealed class Volume : ISimpleAudioVolume
        {
            float volume=.42f;bool muted;
            public void SetMasterVolume(float value,ref Guid context){volume=value;}
            public void GetMasterVolume(out float value){value=volume;}
            public void SetMute(bool value,ref Guid context){muted=value;}
            public void GetMute(out bool value){value=muted;}
        }
        static int failures;
        static void Check(string name,bool ok){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
        static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
        static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate { frame.Continue=false; }));Dispatcher.PushFrame(frame);}
        static bool Wait(Func<bool> check){var until=DateTime.UtcNow.AddSeconds(4);do {Pump();if(check())return true;Thread.Sleep(10);}while(DateTime.UtcNow<until);return false;}
        static T Find<T>(DependencyObject root) where T:DependencyObject {if(root is T)return (T)root;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var result=Find<T>(VisualTreeHelper.GetChild(root,i));if(result!=null)return result;}return null;}
        static void Screenshot(Window window,string file)
        {
            window.UpdateLayout();var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(window);
            var encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(var stream=File.Create(file))encoder.Save(stream);
        }
        [STAThread] public static int Main()
        {
            new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mixer-ui-profile-"+Guid.NewGuid().ToString("N"));
            try {using(var controls=new ControlsStore(root))using(var layouts=new MixerLayoutStore(root))using(var editor=new MixerLayoutStore(root)) {
                var control=controls.Current;control.AutoHideMixer=false;controls.Save(control);
                var theme=new ThemeProfile { FontSize=24 };
                var window=new MixerWindow(theme,controls,layouts);
                Check("mixer starts compact without audio or refresh timer",window.Width==340&&window.Height==420&&window.FontSize==12&&!window.HasAudio&&!window.RefreshRunning);
                window.ShowAtCursor();Pump();((DispatcherTimer)Field(window,"timer")).Stop();
                var rowType=typeof(MixerWindow).GetNestedType("ChannelRow",BindingFlags.NonPublic);
                var channel=new AudioChannel("fixture","Тестовое приложение",0,new Volume(),false);
                var row=(Border)Activator.CreateInstance(rowType,BindingFlags.Instance|BindingFlags.NonPublic,null,new object[]{channel,false,false,layouts.Current},null);
                var rows=(StackPanel)Field(window,"rows");rows.Children.Add(row);((IDictionary)Field(window,"views")).Add("fixture",row);window.UpdateLayout();
                var slider=(Slider)Field(row,"slider");var name=(TextBlock)Field(row,"name");
                slider.ApplyTemplate();window.UpdateLayout();var track=(Track)slider.Template.FindName("PART_Track",slider);
                Check("mixer real row uses compact slider thumb and spacing",slider.Height==24&&track.Thumb.Width==16&&row.Margin.Bottom==8&&name.FontSize==12);
                Screenshot(window,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mixer-compact.png"));
                var p=editor.Current;p.WindowWidth=390;p.WindowHeight=460;p.BodyFontSize=15;p.TitleFontSize=22;p.SliderHeight=36;p.TrackThickness=6;p.ThumbSize=26;p.RowSpacing=13;p.SliderWidth=120;p.IconSize=32;editor.Save(p);
                Check("mixer independent editor changes live row without rebuilding",Wait(delegate{return window.Width==390&&slider.Height==36&&track.Thumb.Width==26&&row.Margin.Bottom==13;})&&Object.ReferenceEquals(((IDictionary)Field(window,"views"))["fixture"],row)&&name.FontSize==15&&slider.MaxWidth==120);
                window.ApplyTheme(new ThemeProfile { FontSize=30 });window.UpdateLayout();
                Check("mixer layout fonts survive theme application",window.FontSize==15&&name.FontSize==15&&((TextBlock)Field(window,"titleText")).FontSize==22);
                var borders=Find<Border>(track.DecreaseRepeatButton);Check("mixer live track thickness applies",borders!=null&&borders.Height==6);
                Screenshot(window,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mixer-custom.png"));
                window.Hide();Check("mixer hide releases rows and stops refresh",!window.RefreshRunning&&window.RowCount==0&&!window.HasAudio);
                editor.ResetDefaults();Check("mixer hidden layout updates without opening audio",Wait(delegate{return window.Width==340&&window.FontSize==12;})&&!window.HasAudio&&!window.RefreshRunning);
                window.Close();p=editor.Current;p.WindowWidth=410;editor.Save(p);Pump();Check("mixer ignores changes after close",window.Width==340);
                channel.Dispose();
#if MIXER_LAYOUT_EDITOR_UI_FIXTURE
                using(var themes=new ThemeStore(root)) {
                    var settings=new ReFix.Hub.MixerLayoutWindow(themes,editor);settings.Show();Pump();
                    var fields=(IList)Field(settings,"fields");var first=fields[0];var size=(Slider)Field(first,"Slider");
                    size.Value=450;Check("mixer editor slider saves live",editor.Current.WindowWidth==450);
                    p=layouts.Current;p.WindowWidth=470;layouts.Save(p);
                    Check("mixer editor follows independent updates",Wait(delegate{return size.Value==470;}));
                    Screenshot(settings,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"mixer-layout-editor.png"));
                    var number=(TextBox)Field(first,"Number");number.Text="NaN";number.Focus();settings.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));Pump();
                    Check("mixer editor rejects nonfinite numeric input",editor.Current.WindowWidth==470);
                    settings.Close();
                }
#endif
            }}catch(Exception ex){Console.WriteLine(ex);failures++;}finally { if(Directory.Exists(root))Directory.Delete(root,true); }
            return failures==0?0:1;
        }
    }
}
#endif
