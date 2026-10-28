using System;
using System.IO;
using System.Threading;
using ReFix.Shared;
namespace ReFix.Tests
{
    public static class MixerLayoutTests
    {
        public static void Run(Action<string,bool> report)
        {
            var p=new MixerLayoutProfile();report("mixer compact defaults",p.WindowWidth==340&&p.WindowHeight==420&&p.BodyFontSize==12&&p.SliderWidth==0);
            p.WindowWidth=Double.NaN;p.WindowHeight=Double.PositiveInfinity;p.BodyFontSize=100;p.TitleFontSize=-4;p.SliderWidth=-1;p.ThumbSize=40;p.SliderHeight=16;p.TrackThickness=200;p.RowSpacing=-2;p.IconSize=99;p.Normalize();
            report("mixer bounds reject nonfinite and excessive dimensions",p.WindowWidth==340&&p.WindowHeight==420&&p.BodyFontSize==24&&p.TitleFontSize==14&&p.SliderWidth==0&&p.SliderHeight==40&&p.TrackThickness==12&&p.RowSpacing==0&&p.IconSize==40);
            var clone=p.Clone();clone.BodyFontSize=10;report("mixer layout snapshots clone",p.BodyFontSize==24);
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"refix-mixer-layout-tests-"+Guid.NewGuid().ToString("N"));
            try { using(var first=new MixerLayoutStore(root))using(var second=new MixerLayoutStore(root)) {
                using(var changed=new ManualResetEvent(false)) {
                    second.Changed+=delegate { changed.Set(); };p=new MixerLayoutProfile { WindowWidth=390,SliderHeight=30,ThumbSize=22 };first.Save(p);p.WindowWidth=700;
                    report("mixer save owns a snapshot",first.Current.WindowWidth==390);
                    report("mixer atomic save updates independent reader",changed.WaitOne(4000)&&second.Current.WindowWidth==390&&second.Current.ThumbSize==22);
                    using(var reopened=new MixerLayoutStore(root))report("mixer layout persists on reopen",reopened.Current.SliderHeight==30);
                    File.WriteAllText(Path.Combine(root,"mixer-layout.json"),"{broken");Thread.Sleep(650);report("mixer malformed input retains last good layout",second.Current.WindowWidth==390);
                    changed.Reset();first.ResetDefaults();report("mixer reset defaults broadcasts live",changed.WaitOne(4000)&&second.Current.WindowWidth==340&&second.Current.SliderHeight==24);
                    int notifications=0;second.Changed+=delegate { Interlocked.Increment(ref notifications); };Thread.Sleep(250);int before=notifications;Thread.Sleep(250);report("mixer watcher stays quiet when unchanged",notifications==before);
                }
            }} finally { if(Directory.Exists(root))Directory.Delete(root,true); }
        }
#if MIXER_LAYOUT_STANDALONE
        [STAThread] public static int Main(){int failures=0;Run(delegate(string name,bool ok){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;});return failures==0?0:1;}
#endif
    }
}
