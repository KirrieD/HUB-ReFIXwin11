using System;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using ReFix.Shared;

internal static class ModuleThemeTests
{
    [STAThread]
    private static int Main()
    {
        try
        {
            ThemeProfile first=new ThemeProfile { Mode="Custom", Accent="#123456", BackgroundOpacity=.4,ImageBlur=8,GlowOpacity=.5,GlowRadius=9,CornerRadius=16 };
            QuietMixer.MixerWindow window=new QuietMixer.MixerWindow(first);
            StackPanel rows=(StackPanel)typeof(QuietMixer.MixerWindow).GetField("rows",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(window);
            Slider sentinel=new Slider { Maximum=100,Value=37 };
            int changes=0; sentinel.ValueChanged+=delegate { changes++; };
            rows.Children.Add(sentinel);
            ContextMenu menu=(ContextMenu)typeof(QuietMixer.MixerWindow).GetMethod("BuildMenu",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(window,null);
            ThemeProfile second=first.Clone(); second.Accent="#ABCDEF"; second.Surface="#334455";
            window.ApplyTheme(second);
            Require(!window.IsVisible,"Theme update must not show mixer");
            Require(!window.RefreshRunning && !window.HasAudio,"Hidden theme update must not start audio or refresh timer");
            Require(rows.Children.Count==1 && Object.ReferenceEquals(rows.Children[0],sentinel),"Theme update must preserve rows");
            Require(sentinel.Value==37 && changes==0,"Theme update must not reset slider values");
            Require(Object.ReferenceEquals(menu.Resources,window.Resources),"Popup menus must use live shared resources");
            Require(((System.Windows.Media.SolidColorBrush)menu.FindResource("Hub.Accent")).Color.ToString()=="#FFABCDEF","Open menu resources must receive updated theme");
            string listener=(string)typeof(QuietMixer.Program).GetField("ListenerName",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            Require(listener==ModuleRuntime.PipeName("quiet-mixer")+".Listener","Mixer listener must share current-user/data-root runtime identity");
            Require(listener!="QuietMixer.Listener.1","Bundled mixer must not control standalone mixer");
            VerifySurface(window);
            string priorRoot=Environment.GetEnvironmentVariable("REFIX_DATA_ROOT");
            try {
                Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ModuleThemeTests."+Guid.NewGuid().ToString("N")));
                ReFix.QuickNote.NoteWindow note=new ReFix.QuickNote.NoteWindow(second);
                VerifySurface(note);
                TextBox text=(TextBox)typeof(ReFix.QuickNote.NoteWindow).GetField("editor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(note);
                text.Text="Локальная заметка · persistence test";
                text.RaiseEvent(new TextChangedEventArgs(TextBox.TextChangedEvent,UndoAction.None));
                note.Save();
                string saveStatus=((TextBlock)typeof(ReFix.QuickNote.NoteWindow).GetField("status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(note)).Text;
                Require(saveStatus.StartsWith("Сохранено"),"Note save failed: "+saveStatus);
                string noteFile=System.IO.Path.Combine(Paths.Root,"notes","quick-note.txt");
                Require(System.IO.File.ReadAllText(noteFile)==text.Text,"Quick Note must persist text");
                ReFix.QuickNote.NoteWindow reopened=new ReFix.QuickNote.NoteWindow(second);
                TextBox restored=(TextBox)typeof(ReFix.QuickNote.NoteWindow).GetField("editor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(reopened);
                Require(restored.Text==text.Text,"Quick Note must restore saved text");
                System.IO.File.Delete(noteFile);
                System.IO.Directory.Delete(System.IO.Path.GetDirectoryName(noteFile));
                System.IO.Directory.Delete(Paths.Root);
            } finally { Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",priorRoot); }
            window.Close();
            Console.WriteLine("PASS: hidden theme, preserved rows/volume, menu resources, XAML templates, both module alpha/glow/blur/corners");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static void VerifySurface(Window window)
    {
        Require(window.AllowsTransparency && window.WindowStyle==WindowStyle.None,"Module must support desktop alpha");
        Require(window.Opacity==1,"Text must remain fully opaque");
        Require(((System.Windows.Media.SolidColorBrush)window.Background).Color.A==0,"Window background must not stack alpha");
        ReFix.Modules.ThemedSurface surface=(ReFix.Modules.ThemedSurface)window.Content;
        Require(Math.Abs(surface.BackgroundLayer.Background.Opacity-.4)<.001,"Background brush must retain configured alpha");
        Require(surface.ImageLayer.Effect is System.Windows.Media.Effects.BlurEffect,"Image blur must be applied only to image layer");
        Require(surface.Frame.Effect is System.Windows.Media.Effects.DropShadowEffect,"Outer frame must render configured glow");
        Require(surface.Frame.CornerRadius.TopLeft==16,"Frame must render configured corners");
    }
    private static void Require(bool condition,string message) { if(!condition) throw new Exception(message); }
}
