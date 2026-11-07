using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Windows.Documents;
using ReFix.QuickNote;
using System.Windows;
using System.Windows.Controls;
using ReFix.Shared;

internal static class ModuleThemeTests
{
    [STAThread]
    private static int Main()
    {
        string originalRoot=Environment.GetEnvironmentVariable("REFIX_DATA_ROOT");
        string isolatedRoot=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ModuleAllTests."+Guid.NewGuid().ToString("N"));
        Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",isolatedRoot);
        try
        {
            VerifyControls();
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
                var directory=Path.Combine(Paths.Root,"notes");
                Directory.CreateDirectory(directory);
                string legacy=Path.Combine(directory,"quick-note.txt");
                File.WriteAllText(legacy,"Старая заметка");
                using(var noteControls=new ControlsStore(Paths.Root))
                {
                var library=new Library(Paths.Root);
                var record=library.Index.Notes.Single();
                var other=library.AddList("Работа");
                other.X=1530;other.Y=160;other.Collapsed=true;
                library.Index.Colors.Add("#ABCDEF");
                library.Move(record,other);
                var savedLibrary=new Library(Paths.Root);
                Require(savedLibrary.Index.Notes.Single().ListId==other.Id,"Note transfer must persist single membership");
                var savedList=savedLibrary.Index.Lists.Single(l=>l.Id==other.Id);
                Require(savedList.X==1530 && savedList.Y==160 && savedList.Collapsed && savedLibrary.Index.Colors.Single()=="#ABCDEF","List placement, collapse and recent colors must survive restart");
                var note=new NoteWindow(library,record,second,noteControls,null,null);
                VerifySurface(note);
                var text=Editor(note);
                Require(Plain(text)=="Старая заметка" && File.ReadAllText(legacy)=="Старая заметка","Import must retain legacy text");
                var paragraph=new Paragraph(new Run("Локальная заметка") { Foreground=System.Windows.Media.Brushes.Red, Background=System.Windows.Media.Brushes.Yellow, FontSize=28 });
                text.Document.Blocks.Clear();
                text.Document.Blocks.Add(paragraph);
                var image=System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[]{0,0,255,255},4);
                text.CaretPosition=paragraph.ContentEnd;
                typeof(NoteWindow).GetMethod("InsertImage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(note,new object[]{new Image{Source=image}});
                Require(note.Save(),"Note save failed");
                using(var reopened=new NoteWindow(library,record,second,noteControls,null,null))
                {
                    var restored=(Paragraph)Editor(reopened).Document.Blocks.FirstBlock;
                    Require(Plain(Editor(reopened)).StartsWith("Локальная заметка"),"Quick Note must restore saved text");
                    Require(restored.Inlines.OfType<Run>().Any(r=>r.FontSize==28&&((System.Windows.Media.SolidColorBrush)r.Foreground).Color==System.Windows.Media.Colors.Red&&((System.Windows.Media.SolidColorBrush)r.Background).Color==System.Windows.Media.Colors.Yellow),"Rich formatting must survive save and reopen");
                    Require(restored.Inlines.OfType<InlineUIContainer>().Any(i=>i.Child is Image),"Image must survive save and reopen");
                }
                note.Dispose();
                var secondRecord=library.AddNote(library.Index.Lists[0]);
                using(var noteTheme=new ThemeStore(Paths.Root))
                using(var controller=new NoteController(library,noteTheme,noteControls))
                {
                    controller.Show();
                    controller.Open(record);
                    controller.Open(secondRecord);
                    var fields=BindingFlags.Instance|BindingFlags.NonPublic;
                    var windows=(System.Collections.Generic.Dictionary<string,NoteWindow>)typeof(NoteController).GetField("editors",fields).GetValue(controller);
                    var lists=(System.Collections.Generic.Dictionary<string,ListWindow>)typeof(NoteController).GetField("lists",fields).GetValue(controller);
                    Require(windows.Count==2&&windows.Values.All(w=>w.IsVisible)&&lists.Count==2,"Two notes and two lists must be independently visible");
                    windows[record.Id].MinimizeNote();
                    Require(!windows[record.Id].IsVisible&&windows[secondRecord.Id].IsVisible&&lists[other.Id].IsVisible,"Minimizing a note restores its list and leaves other notes open");
                    var noteRows=(StackPanel)typeof(ListWindow).GetField("notes",fields).GetValue(lists[other.Id]);
                    var row=(Button)noteRows.Children[0];
                    var move=(MenuItem)row.ContextMenu.Items[0];
                    ((MenuItem)move.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Require(record.ListId==library.Index.Lists[0].Id&&noteRows.Children[0] is TextBlock,"Right-click transfer must remove the row from its previous list");
                }
                }
                System.IO.Directory.Delete(Paths.Root,true);
            } finally { Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",priorRoot); }
            window.Close();
            Console.WriteLine("PASS: hidden theme, preserved rows/volume, menu resources, XAML templates, both module alpha/glow/blur/corners");
            return 0;
        }
        catch(Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally{Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",originalRoot);if(System.IO.Directory.Exists(isolatedRoot))System.IO.Directory.Delete(isolatedRoot,true);}
    }
    static RichTextBox Editor(NoteWindow note){return (RichTextBox)typeof(NoteWindow).GetField("editor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(note);}
    static string Plain(RichTextBox editor){return new TextRange(editor.Document.ContentStart,editor.Document.ContentEnd).Text.TrimEnd('\r','\n');}
    private static void VerifyControls()
    {
        string root=System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ModuleControls."+Guid.NewGuid().ToString("N"));
        string prior=Environment.GetEnvironmentVariable("REFIX_DATA_ROOT");Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",root);
        try { using(var controls=new ControlsStore(root)) {
            var signature=new[]{typeof(ThemeProfile),typeof(ControlsStore)};
            var mixerConstructor=typeof(QuietMixer.MixerWindow).GetConstructor(BindingFlags.Instance|BindingFlags.NonPublic,null,signature,null);
            var library=new Library(root);var record=library.AddNote(library.Index.Lists[0]);
            Require(mixerConstructor!=null,"Both existing module windows must accept shared live controls");
            var mixer=(QuietMixer.MixerWindow)mixerConstructor.Invoke(new object[]{new ThemeProfile(),controls});
            var note=new NoteWindow(library,record,new ThemeProfile(),controls,null,null);
            var rows=(StackPanel)typeof(QuietMixer.MixerWindow).GetField("rows",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mixer);
            var slider=new Slider{Value=37};rows.Children.Add(slider);
            var editor=Editor(note);new TextRange(editor.Document.ContentStart,editor.Document.ContentEnd).Text="Preserve unsaved note during controls updates";
            controls.Assign("quick-note.save","Ctrl+Shift+S");controls.Assign("quiet-mixer.hide","Ctrl+Q");controls.Assign("quick-note.hide","Ctrl+Q");
            var frame=new System.Windows.Threading.DispatcherFrame();System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));System.Windows.Threading.Dispatcher.PushFrame(frame);
            Require(Plain(editor)=="Preserve unsaved note during controls updates"&&slider.Value==37&&rows.Children.Contains(slider),"Live controls must preserve note and volume state");
            var noteStatus=(TextBlock)typeof(ReFix.QuickNote.NoteWindow).GetField("status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(note);
            var mixerStatus=(TextBlock)typeof(QuietMixer.MixerWindow).GetField("status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mixer);
            DateTime until=DateTime.UtcNow.AddSeconds(3);while(DateTime.UtcNow<until&&!noteStatus.Text.Contains("Ctrl+Shift+S")){frame=new System.Windows.Threading.DispatcherFrame();System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));System.Windows.Threading.Dispatcher.PushFrame(frame);System.Threading.Thread.Sleep(10);}
            Require(noteStatus.Text.Contains("Ctrl+Shift+S")&&mixerStatus.Text.Contains("Ctrl+Q"),"Existing window help must reflect changed bindings");
            controls.Assign("quick-note.save","F21");controls.Assign("quiet-mixer.hide","F22");controls.Assign("quick-note.hide","F22");
            using(var source=new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("Isolated local controls"){Width=0,Height=0})) {
                var key=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.F21){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};note.RaiseEvent(key);
                var saved=new FlowDocument();library.LoadBody(record,saved);Require(key.Handled&&new TextRange(saved.ContentStart,saved.ContentEnd).Text.Contains("Preserve unsaved note"),"Rebound save must execute on the existing note window");
                key=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.Escape){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};mixer.RaiseEvent(key);Require(!key.Handled,"Old Escape binding must stop executing immediately");
                key=new System.Windows.Input.KeyEventArgs(System.Windows.Input.Keyboard.PrimaryDevice,source,0,System.Windows.Input.Key.F22){RoutedEvent=System.Windows.Input.Keyboard.PreviewKeyDownEvent};mixer.RaiseEvent(key);Require(key.Handled,"Rebound hide must execute on existing mixer");
            }
            mixer.Close();((IDisposable)(object)note).Dispose();
        }}finally{Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",prior);if(System.IO.Directory.Exists(root))System.IO.Directory.Delete(root,true);}
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
