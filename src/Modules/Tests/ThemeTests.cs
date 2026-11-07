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
                var note=new NoteEditor(library,record,noteControls,null,null);
                var text=Editor(note);
                Require(Plain(text)=="Старая заметка" && File.ReadAllText(legacy)=="Старая заметка","Import must retain legacy text");
                var paragraph=new Paragraph(new Run("Локальная заметка") { Foreground=System.Windows.Media.Brushes.Red, Background=System.Windows.Media.Brushes.Yellow, FontSize=28 });
                text.Document.Blocks.Clear();
                text.Document.Blocks.Add(paragraph);
                var image=System.Windows.Media.Imaging.BitmapSource.Create(1,1,96,96,System.Windows.Media.PixelFormats.Bgra32,null,new byte[]{0,0,255,255},4);
                text.CaretPosition=paragraph.ContentEnd;
                typeof(NoteEditor).GetMethod("InsertImage",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(note,new object[]{new Image{Source=image}});
                var ordered=new System.Windows.Documents.List { MarkerStyle=TextMarkerStyle.Decimal };
                ordered.ListItems.Add(new ListItem(new Paragraph(new Run("Первый пункт"))));
                var bullets=new System.Windows.Documents.List { MarkerStyle=TextMarkerStyle.Disc };
                bullets.ListItems.Add(new ListItem(new Paragraph(new Run("Маркер"))));
                text.Document.Blocks.Add(ordered);
                text.Document.Blocks.Add(bullets);
                var decorated=paragraph.Inlines.OfType<Run>().First();
                decorated.TextDecorations=new TextDecorationCollection { System.Windows.TextDecorations.Underline[0], System.Windows.TextDecorations.Strikethrough[0] };
                Require(note.Save(),"Note save failed");
                using(var reopened=new NoteEditor(library,record,noteControls,null,null))
                {
                    var restored=(Paragraph)Editor(reopened).Document.Blocks.FirstBlock;
                    Require(Plain(Editor(reopened)).StartsWith("Локальная заметка"),"Quick Note must restore saved text");
                    Require(restored.Inlines.OfType<Run>().Any(r=>r.FontSize==28&&((System.Windows.Media.SolidColorBrush)r.Foreground).Color==System.Windows.Media.Colors.Red&&((System.Windows.Media.SolidColorBrush)r.Background).Color==System.Windows.Media.Colors.Yellow),"Rich formatting must survive save and reopen");
                    Require(Editor(reopened).Document.Blocks.OfType<System.Windows.Documents.List>().Select(l=>l.MarkerStyle).SequenceEqual(new[]{TextMarkerStyle.Decimal,TextMarkerStyle.Disc}),"Both list types must survive reopening");
                    Require(restored.Inlines.OfType<Run>().Any(r=>r.TextDecorations.Count==2),"Strike and underline must survive together");
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
                    var windows=(System.Collections.Generic.Dictionary<string,NoteWorkspace>)typeof(NoteController).GetField("workspaces",fields).GetValue(controller);
                    var lists=(System.Collections.Generic.Dictionary<string,ListWindow>)typeof(NoteController).GetField("lists",fields).GetValue(controller);
                    Require(windows.Count==2&&windows.Values.All(w=>w.IsVisible)&&lists.Count==2,"Different lists must have independent windows");
                    var workspace=windows[other.Id];
                    workspace.ApplyTheme(second);
                    VerifySurface(workspace);
                    var editor=Editor(controller.Editor(record));
                    workspace.UpdateLayout();
                    var layer=(RoundedHighlights)((Grid)editor.Parent).Children[1];
                    layer.InvalidateVisual();
                    var rendered=new System.Windows.Media.Imaging.RenderTargetBitmap(950,660,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                    rendered.Render(workspace);
                    var drawing=System.Windows.Media.VisualTreeHelper.GetDrawing(layer);
                    Require(drawing!=null&&!drawing.Bounds.IsEmpty,"Rounded text highlight must render in the tab");
                    Require(lists[other.Id].ActualWidth<150,"Desktop list tab must be compact");
                    var png=new System.Windows.Media.Imaging.PngBitmapEncoder();
                    png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(rendered));
                    using(var file=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"quick-note-preview.png"))) png.Save(file);
                    workspace.Minimize();
                    Require(!workspace.IsVisible&&windows[secondRecord.ListId].IsVisible&&lists[other.Id].IsVisible,"Minimizing one list must leave others open");
                    controller.Move(record.Id,library.Index.Lists[0]);
                    var tabs=(TabControl)typeof(NoteWorkspace).GetField("tabs",fields).GetValue(windows[secondRecord.ListId]);
                    Require(record.ListId==secondRecord.ListId&&tabs.Items.Count==2,"Moved note must appear as a tab in the destination list");
                    tabs.SelectedIndex=0;tabs.SelectedIndex=1;
                    Require(Object.ReferenceEquals(editor,Editor(controller.Editor(record))),"Tab switching must keep the editor and its undo stack");
                    VerifyWorkspaceFeatures(controller.Editor(record),Paths.Root);
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
    static RichTextBox Editor(NoteEditor note){return (RichTextBox)typeof(NoteEditor).GetField("editor",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(note);}
    static string Plain(RichTextBox editor){return new TextRange(editor.Document.ContentStart,editor.Document.ContentEnd).Text.TrimEnd('\r','\n');}
    static void VerifyWorkspaceFeatures(NoteEditor note,string directory)
    {
        var document = new FlowDocument(new Paragraph(new Run("Альфа ")) { Inlines = { new Bold(new Run("бета")), new Run(" альфа") } });
        Require(NoteSearch.Find(document,"альфа",false).Count==2&&NoteSearch.Find(document,"альфа",true).Count==1,"Search must honor case");
        Require(NoteSearch.Find(document,"а бета",false).Single().Text=="а бета","Search must span formatted runs");
        var fields=BindingFlags.Instance|BindingFlags.NonPublic;
        using(var scratchControls=new ControlsStore(Path.Combine(directory,"search-fixture")))
        {
            var library=new Library(Path.Combine(directory,"search-fixture"));
            var record=library.AddNote(library.Index.Lists[0]);
            using(var scratch=new NoteEditor(library,record,scratchControls,null,null))
            {
                var host=new Window { Content=scratch, Width=600, Height=430 }; host.Show(); host.UpdateLayout();
                var editor=Editor(scratch);
                new TextRange(editor.Document.ContentStart,editor.Document.ContentEnd).Text="Альфа альфа";
                typeof(NoteEditor).GetMethod("OpenFind",fields).Invoke(scratch,new object[]{true});
                ((TextBox)typeof(NoteEditor).GetField("query",fields).GetValue(scratch)).Text="альфа";
                ((TextBox)typeof(NoteEditor).GetField("replacement",fields).GetValue(scratch)).Text="бета";
                ((ComboBox)typeof(NoteEditor).GetField("replaceMode",fields).GetValue(scratch)).SelectedIndex=1;
                typeof(NoteEditor).GetMethod("Replace",fields).Invoke(scratch,null);
                Require(Plain(editor)=="бета бета","Replace all must replace both case-insensitive matches");
                bool undone=editor.Undo();
                host.Close();
                Require(undone&&Plain(editor)=="Альфа альфа","Replace all must undo as a single edit; result: "+Plain(editor)+", undo="+undone);
            }
        }
        var rendered = MarkdownNote.Render("# Заголовок\n**Жирный**\n1. Список",n=>null);
        Require(rendered.Blocks.Count==3&&rendered.Blocks.LastBlock is System.Windows.Documents.List,"Markdown preview must render headings and lists");
        var source=NoteDocument.Capture(Editor(note).Document);
        string word=Path.Combine(directory,"test.docx");
        ExportFile.Write(word,".docx",source,Path.Combine(directory,"test_images"));
        using(var file=File.OpenRead(word))
        using(var zip=new System.IO.Compression.ZipArchive(file,System.IO.Compression.ZipArchiveMode.Read))
        {
            Require(zip.GetEntry("word/media/image1.png")!=null&&zip.GetEntry("word/numbering.xml")!=null,"DOCX must embed images and numbering");
            using(var reader=new StreamReader(zip.GetEntry("word/document.xml").Open()))
                Require(reader.ReadToEnd().Contains("Локальная заметка"),"DOCX must contain actual note text");
        }
        var paginator=(DocumentPaginator)typeof(NoteEditor).GetMethod("Paginate",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(note,new object[]{new Size(793,1122)});
        paginator.ComputePageCount();
        Require(paginator.PageCount>0&&paginator.GetPage(0).Visual!=null,"Preview must paginate the real document");
    }
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
            var note=new NoteEditor(library,record,controls,null,null);
            var rows=(StackPanel)typeof(QuietMixer.MixerWindow).GetField("rows",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mixer);
            var slider=new Slider{Value=37};rows.Children.Add(slider);
            var editor=Editor(note);new TextRange(editor.Document.ContentStart,editor.Document.ContentEnd).Text="Preserve unsaved note during controls updates";
            controls.Assign("quick-note.save","Ctrl+Shift+S");controls.Assign("quiet-mixer.hide","Ctrl+Q");controls.Assign("quick-note.hide","Ctrl+Q");
            var frame=new System.Windows.Threading.DispatcherFrame();System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));System.Windows.Threading.Dispatcher.PushFrame(frame);
            Require(Plain(editor)=="Preserve unsaved note during controls updates"&&slider.Value==37&&rows.Children.Contains(slider),"Live controls must preserve note and volume state");
            var noteStatus=(TextBlock)typeof(ReFix.QuickNote.NoteEditor).GetField("status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(note);
            var mixerStatus=(TextBlock)typeof(QuietMixer.MixerWindow).GetField("status",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(mixer);
            DateTime until=DateTime.UtcNow.AddSeconds(3);while(DateTime.UtcNow<until&&!Convert.ToString(noteStatus.ToolTip).Contains("Ctrl+Shift+S")){frame=new System.Windows.Threading.DispatcherFrame();System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));System.Windows.Threading.Dispatcher.PushFrame(frame);System.Threading.Thread.Sleep(10);}
            Require(Convert.ToString(noteStatus.ToolTip).Contains("Ctrl+Shift+S")&&mixerStatus.Text.Contains("Ctrl+Q"),"Existing window help must reflect changed bindings");
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
