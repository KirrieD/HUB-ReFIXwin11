using System;
using System.Reflection;
using System.IO;
using System.Linq;
using System.Windows.Documents;
using ReFix.QuickNote;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
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
            Require(menu.Resources.MergedDictionaries.Contains(window.Resources),"Popup menus must use live shared resources");
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
                    var noteEditor=controller.Editor(record);
                    var editorMenu=((DockPanel)noteEditor.Content).Children.OfType<Menu>().Single();
                    var fileMenu=(MenuItem)editorMenu.Items[0];
                    fileMenu.ApplyTemplate();
                    var popup=(System.Windows.Controls.Primitives.Popup)fileMenu.Template.FindName("PART_Popup",fileMenu);
                    Require(popup!=null && ((Border)popup.Child).BorderThickness==new Thickness(0),"Note menu must have no system popup border");
                    fileMenu.IsSubmenuOpen=true;
                    Require(popup.IsOpen,"File menu opens using the themed popup");
                    fileMenu.IsSubmenuOpen=false;
                    workspace.ApplyTheme(new ThemeProfile{Mode="Custom",Background="#FFFFFF"});
                    Require(Object.Equals(workspace.Resources["Note.ToggleGlyph"],System.Windows.Media.Brushes.Black),"Light editor uses a black toggle glyph");
                    workspace.ApplyTheme(new ThemeProfile{Mode="Custom",Background="#202020"});
                    Require(Object.Equals(workspace.Resources["Note.ToggleGlyph"],System.Windows.Media.Brushes.White),"Dark editor uses a white toggle glyph");
                    workspace.ApplyTheme(second);
                    var context=ThemeMenu.Create(workspace.Resources);
                    context.Items.Add(new MenuItem { Header = "Новая заметка" });
                    context.PlacementTarget=workspace;
                    context.IsOpen=true;
                    context.ApplyTemplate();
                    Require(System.Windows.Media.VisualTreeHelper.GetChild(context,0) is Border && ((Border)System.Windows.Media.VisualTreeHelper.GetChild(context,0)).BorderThickness==new Thickness(0),"Context menu must have no system side gutter or border");
                    context.IsOpen=false;
                    var editor=Editor(noteEditor);
                    workspace.UpdateLayout();
                    var layer=(RoundedHighlights)((Grid)editor.Parent).Children[1];
                    layer.InvalidateVisual();
                    var rendered=new System.Windows.Media.Imaging.RenderTargetBitmap(950,660,96,96,System.Windows.Media.PixelFormats.Pbgra32);
                    rendered.Render(workspace);
                    var drawing=System.Windows.Media.VisualTreeHelper.GetDrawing(layer);
                    Require(drawing!=null&&!drawing.Bounds.IsEmpty,"Rounded text highlight must render in the tab");
                    var highlighted=NoteDocument.Paragraphs(editor.Document.Blocks).SelectMany(p=>NoteDocument.Runs(p.Inlines)).First(run=>!String.IsNullOrEmpty(run.Text));
                    var range=new TextRange(highlighted.ContentStart,highlighted.ContentEnd);
                    typeof(NoteEditor).GetMethod("ApplyColor",fields).Invoke(noteEditor,new object[]{range,TextElement.BackgroundProperty,"#199B72"});
                    var renderFrame=new System.Windows.Threading.DispatcherFrame();
                    workspace.Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded,new Action(delegate{renderFrame.Continue=false;}));
                    System.Windows.Threading.Dispatcher.PushFrame(renderFrame);
                    rendered.Render(workspace);
                    drawing=System.Windows.Media.VisualTreeHelper.GetDrawing(layer);
                    Require(drawing!=null&&ContainsHighlight(drawing,System.Windows.Media.Color.FromRgb(25,155,114)),"Palette change redraws the rounded highlight without leaving the editor");
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
                    controller.Open(record);
                    VerifyImmediateFormatting(controller.Editor(record));
                    VerifyListEditing(controller.Editor(record));
                    VerifyParagraphSpacing(controller.Editor(record));
                    var targetTab=tabs.Items.OfType<TabItem>().Single(t=>t.Tag==record);
                    var close=((DockPanel)targetTab.Header).Children.OfType<Button>().Single();
                    close.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
                    Require(tabs.Items.Count==1&&((TabItem)tabs.SelectedItem).Tag==secondRecord,"Closing the active note selects the remaining tab");
                    var onDisk=new Library(Paths.Root);
                    Require(onDisk.Index.Notes.Count==2&&onDisk.Index.Notes.Single(n=>n.Id==record.Id).TabClosed,"Closing saves the tab status without removing the note");
                    controller.Open(record);
                    Require(tabs.Items.Count==2&&!record.TabClosed&&Plain(Editor(controller.Editor(record))).Contains("Первый"),"Reopening restores the saved text and the tab");
                    var tabMenu=((DockPanel)tabs.Items.OfType<TabItem>().Single(t=>t.Tag==secondRecord).Header).ContextMenu;
                    tabMenu.IsOpen=true;
                    var closeFromMenu=tabMenu.Items.OfType<MenuItem>().Single(i=>Equals(i.Header,"Закрыть заметку"));
                    tabMenu.IsOpen=false;
                    closeFromMenu.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
                    Require(tabs.Items.Count==1&&((TabItem)tabs.SelectedItem).Tag==record&&secondRecord.TabClosed,"Context menu closes only the chosen tab");
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
    static bool ContainsHighlight(System.Windows.Media.Drawing drawing,System.Windows.Media.Color color)
    {
        var group=drawing as System.Windows.Media.DrawingGroup;
        if(group!=null)return group.Children.Any(item=>ContainsHighlight(item,color));
        var geometry=drawing as System.Windows.Media.GeometryDrawing;
        var brush=geometry==null?null:geometry.Brush as System.Windows.Media.SolidColorBrush;
        return brush!=null&&brush.Color==color;
    }

    static void VerifyListEditing(NoteEditor note)
    {
        var editor=Editor(note);
        var fields=BindingFlags.Instance|BindingFlags.NonPublic;
        var startList=typeof(NoteEditor).GetMethod("TryStartList",fields);
        var changeIndent=typeof(NoteEditor).GetMethod("ChangeIndent",fields);
        foreach(var prefix in new[]{"3.","-","*"})
        {
            var paragraph=new Paragraph(new Run(prefix));
            editor.Document=new FlowDocument(paragraph);
            editor.Focus();
            editor.CaretPosition=paragraph.ContentEnd;
            Require((bool)startList.Invoke(note,null),"A typed list prefix starts a list: "+prefix);
            var list=(System.Windows.Documents.List)editor.Document.Blocks.FirstBlock;
            var first=(Paragraph)list.ListItems.FirstListItem.Blocks.FirstBlock;
            var content=String.Concat(NoteDocument.Runs(first.Inlines).Select(run=>run.Text));
            Require(content==""&&list.Padding.Left==24,"The marker is converted into an empty list item at the fixed step. Text: ["+content+"] Padding: "+list.Padding.Left);
            Require(list.MarkerStyle==(prefix=="3."?TextMarkerStyle.Decimal:TextMarkerStyle.Disc),"The marker chooses the correct list type");
            Require(prefix!="3."||list.StartIndex==3,"Numbering begins at the typed number");
            changeIndent.Invoke(note,new object[]{true});
            Require(list.ListItems.FirstListItem.Margin.Left==24,"A first list item can move down a level at the selected step");
            var reopened=new FlowDocument();
            NoteDocument.Restore(reopened,NoteDocument.Capture(editor.Document));
            var restoredList=(System.Windows.Documents.List)reopened.Blocks.FirstBlock;
            Require(restoredList.ListItems.FirstListItem.Margin.Left==24,"The first item keeps its custom indent when reopened");
            changeIndent.Invoke(note,new object[]{false});
            Require(list.ListItems.FirstListItem.Margin.Left==0,"The first item can return to its original level");
            list=(System.Windows.Documents.List)editor.Document.Blocks.FirstBlock;
            var next=new Paragraph(new Run("Пункт"));
            list.ListItems.Add(new ListItem(next));
            editor.CaretPosition=next.ContentStart;
            changeIndent.Invoke(note,new object[]{true});
            var parent=(System.Windows.Documents.List)((ListItem)next.Parent).Parent;
            Require(parent!=list&&parent.Padding.Left==24,"Indenting a following item creates a nested list at the chosen step");
            changeIndent.Invoke(note,new object[]{false});
            Require(((ListItem)next.Parent).Parent is System.Windows.Documents.List,"Outdenting keeps the item in the list");
        }
        var single=new Paragraph(new Run("Абзац"));
        editor.Document=new FlowDocument(single);
        editor.CaretPosition=single.ContentStart;
        changeIndent.Invoke(note,new object[]{true});
        var restored=new FlowDocument();
        NoteDocument.Restore(restored,NoteDocument.Capture(editor.Document));
        Require(((Paragraph)restored.Blocks.FirstBlock).Margin.Left==24,"Paragraph indentation survives save and restore");
        changeIndent.Invoke(note,new object[]{false});
        Require(single.Margin.Left==0,"Indentation can be reduced");
        var field=ToolbarGroups(note).SelectMany(panel=>panel.Children.OfType<StackPanel>())
            .SelectMany(panel=>panel.Children.OfType<TextBox>()).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Шаг отступа");
        field.Text="37";
        var range=typeof(NoteEditor).GetMethod("TrySetIndentStep",fields);
        Require((bool)range.Invoke(note,new object[]{field.Text}),"A custom step outside the presets must be accepted");
        Require(!(bool)range.Invoke(note,new object[]{"0"})&&!(bool)range.Invoke(note,new object[]{"121"}),"Invalid custom steps are rejected");
        changeIndent.Invoke(note,new object[]{true});
        Require(single.Margin.Left==37&&note.Save(),"The custom step must be used and saved");
        var record=(NoteRecord)typeof(NoteEditor).GetField("note",fields).GetValue(note);
        Require(new Library(Paths.Root).Index.Notes.Single(item=>item.Id==record.Id).IndentStep==37,"Custom step survives reopening the library");
        var customItem=new Paragraph(new Run("Первый"));
        var listCustom=new System.Windows.Documents.List(new ListItem(customItem));
        editor.Document=new FlowDocument(listCustom);
        editor.CaretPosition=customItem.ContentStart;
        changeIndent.Invoke(note,new object[]{true});
        Require(listCustom.Padding.Left==37&&listCustom.ListItems.FirstListItem.Margin.Left==37,"Custom steps also apply to list indentation");
    }

    static System.Collections.Generic.IEnumerable<WrapPanel> ToolbarGroups(NoteEditor note)
    {
        var toolbar=((DockPanel)note.Content).Children.OfType<ScrollViewer>().Single();
        return ((WrapPanel)toolbar.Content).Children.OfType<Border>()
            .SelectMany(frame=>((StackPanel)frame.Child).Children.OfType<WrapPanel>());
    }

    static void VerifyParagraphSpacing(NoteEditor note)
    {
        var editor=Editor(note);
        var first=new Paragraph(new Run("Первый")) { Margin=new Thickness(32,0,0,0) };
        var second=new Paragraph(new Run("Второй"));
        var untouched=new Paragraph(new Run("Третий"));
        editor.Document=new FlowDocument { Blocks={first,second,untouched} };
        editor.Selection.Select(first.ContentStart,second.ContentEnd);
        var fields=ToolbarGroups(note).SelectMany(panel=>panel.Children.OfType<StackPanel>())
            .SelectMany(panel=>panel.Children.OfType<TextBox>()).ToDictionary(t=>System.Windows.Automation.AutomationProperties.GetName(t));
        Require(ToolbarGroups(note).Count()==7&&fields.ContainsKey("Интервал перед абзацем")&&fields.ContainsKey("Интервал после абзаца"),"Formatting commands are grouped and have both spacing inputs");
        foreach(var choice in new[]{new{Label="Интервал перед абзацем",Value="18"},new{Label="Интервал после абзаца",Value="7"}})
        {
            var field=fields[choice.Label];
            var keyboard=System.Windows.Input.Keyboard.PrimaryDevice;
            field.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(keyboard,0,editor,field)
                {RoutedEvent=System.Windows.Input.Keyboard.GotKeyboardFocusEvent});
            field.Text=choice.Value;
            field.RaiseEvent(new System.Windows.Input.KeyboardFocusChangedEventArgs(keyboard,0,field,editor)
                {RoutedEvent=System.Windows.Input.Keyboard.LostKeyboardFocusEvent});
        }
        Require(first.Margin.Top==18&&second.Margin.Top==18&&first.Margin.Bottom==7&&second.Margin.Bottom==7,
            "Spacing applies immediately to all selected paragraphs");
        Require(first.Margin.Left==32&&Double.IsNaN(untouched.Margin.Top)&&!editor.Selection.IsEmpty,
            "Vertical spacing preserves horizontal indentation, adjacent paragraphs and selection");
        var restored=new FlowDocument();
        var blocks=NoteDocument.Capture(editor.Document);
        NoteDocument.Restore(restored,blocks);
        var saved=(Paragraph)restored.Blocks.FirstBlock;
        Require(saved.Margin.Top==18&&saved.Margin.Bottom==7&&saved.Margin.Left==32,"Both vertical offsets survive reopening");
        string html=Path.Combine(Paths.Root,"spacing.html"),word=Path.Combine(Paths.Root,"spacing.docx");
        ExportFile.Write(html,".html",blocks,Path.Combine(Paths.Root,"spacing_images"));
        Require(File.ReadAllText(html).Contains("margin-top:18px;margin-bottom:7px"),"HTML preserves both paragraph intervals");
        ExportFile.Write(word,".docx",blocks,Path.Combine(Paths.Root,"spacing_images"));
        using(var file=File.OpenRead(word))
        using(var zip=new System.IO.Compression.ZipArchive(file,System.IO.Compression.ZipArchiveMode.Read))
        using(var input=zip.GetEntry("word/document.xml").Open())
        {
            var name=System.Xml.Linq.XNamespace.Get("http://schemas.openxmlformats.org/wordprocessingml/2006/main");
            var spacing=System.Xml.Linq.XDocument.Load(input).Descendants(name+"spacing").First();
            Require((string)spacing.Attribute(name+"before")=="270"&&(string)spacing.Attribute(name+"after")=="105","DOCX preserves both paragraph intervals in twips");
        }
    }

    static void VerifyImmediateFormatting(NoteEditor note)
    {
        var editor=Editor(note);
        var selected=new Run("Альфа");
        var tail=new Run(" Бета");
        editor.Document=new FlowDocument(new Paragraph { Inlines={selected,tail} });
        editor.Focus();
        editor.Selection.Select(selected.ContentStart, selected.ContentEnd);
        var toolbar=ToolbarGroups(note).SelectMany(group=>group.Children.Cast<UIElement>());
        var size=toolbar.OfType<ComboBox>().Single(c=>Convert.ToString(c.ToolTip)=="Размер шрифта");
        size.Focus();
        size.SelectedItem=24;
        Require(editor.IsInactiveSelectionHighlightEnabled,"Selection must stay visible when the toolbar gains focus");
        Require(editor.Selection.Text=="Альфа","Changing size must preserve the exact selection");
        Require((double)editor.Selection.GetPropertyValue(TextElement.FontSizeProperty)==32&&tail.FontSize!=32,"Size must affect the existing selection immediately");
        var bold=toolbar.OfType<Button>().Single(b=>Convert.ToString(b.ToolTip)=="Жирный");
        bold.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Require(editor.Selection.Text=="Альфа"&&(FontWeight)editor.Selection.GetPropertyValue(TextElement.FontWeightProperty)==FontWeights.Bold,"The next toolbar command must keep and format the same range");
        var range=new TextRange(editor.Selection.Start,editor.Selection.End);
        typeof(NoteEditor).GetMethod("ApplyColor",BindingFlags.Instance|BindingFlags.NonPublic)
            .Invoke(note,new object[]{range,TextElement.ForegroundProperty,"#22CC77"});
        var color=Color.FromRgb(34,204,119);
        Require(((SolidColorBrush)editor.Selection.GetPropertyValue(TextElement.ForegroundProperty)).Color==color&&
            ((SolidColorBrush)editor.SelectionTextBrush).Color==color&&editor.Selection.Text=="Альфа","The chosen color must be visible on the retained selection");
    }

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
                var saved=new FlowDocument();library.LoadBody(record,saved);Require(key.Handled&&new TextRange(saved.ContentStart,saved.ContentEnd).Text.Contains("Preserve unsaved note"),"Rebound save must execute on the existing note window; handled="+key.Handled+", modifiers="+System.Windows.Input.Keyboard.Modifiers+", binding="+controls.Current.Get("quick-note.save"));
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
