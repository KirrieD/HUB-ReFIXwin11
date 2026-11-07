using System;
using System.IO;
using System.Linq;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media.Imaging;

namespace ReFix.QuickNote
{
    internal sealed partial class NoteEditor
    {
        const string ClipboardFragment = "ReFIX.QuickNote.Fragment.1";
        static JavaScriptSerializer FragmentSerializer() { return new JavaScriptSerializer { MaxJsonLength = 32*1024*1024 }; }

        void SetClipboardFragment(DataObject data)
        {
            using (var package = new MemoryStream())
            {
                editor.Selection.Save(package,DataFormats.XamlPackage);
                if (package.Length > 24*1024*1024) return;
                package.Position = 0;
                var document = new FlowDocument();
                new TextRange(document.ContentStart,document.ContentEnd).Load(package,DataFormats.XamlPackage);
                data.SetData(ClipboardFragment,FragmentSerializer().Serialize(NoteDocument.Capture(document)),false);
            }
        }

        bool PasteNoteFragment(DataObjectPastingEventArgs args)
        {
            if (note.Markdown || !args.DataObject.GetDataPresent(ClipboardFragment,false)) return false;
            var fragment = args.DataObject.GetData(ClipboardFragment,false) as string;
            if (String.IsNullOrEmpty(fragment) || fragment.Length > 32*1024*1024) return false;
            try
            {
                var model = FragmentSerializer().Deserialize<System.Collections.Generic.List<NoteParagraph>>(fragment);
                var document = new FlowDocument();
                NoteDocument.Restore(document,model);
                using(var buffer = new MemoryStream())
                {
                    new TextRange(document.ContentStart,document.ContentEnd).Save(buffer,DataFormats.XamlPackage);
                    buffer.Position = 0;
                    editor.Selection.Load(buffer,DataFormats.XamlPackage);
                }
                args.CancelCommand();
                return true;
            }
            catch (Exception ex)
            {
                status.Text = "Не удалось вставить форматированный фрагмент: " + ex.Message;
                return false;
            }
        }

        void PasteMarkdownBitmap(BitmapSource source)
        {
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using (var bytes = new MemoryStream())
            {
                encoder.Save(bytes);
                string name = library.AddImage(note,bytes.ToArray(),".png");
                InsertText("![Изображение](refix-image:" + name + ")");
            }
        }

        void History()
        {
            ExecuteSafely(delegate
            {
                if (!Save()) return;
                var paths = library.History(note);
                var list = new ComboBox { Margin = new Thickness(0,0,0,8) };
                foreach (string path in paths)
                {
                    long ticks;
                    string tail = path.Substring(path.LastIndexOf('-')+1);
                    var date = Int64.TryParse(tail,out ticks) && ticks >= DateTime.MinValue.Ticks && ticks <= DateTime.MaxValue.Ticks ? new DateTime(ticks,DateTimeKind.Utc).ToLocalTime() : File.GetLastWriteTime(path);
                    list.Items.Add(new ComboBoxItem { Content = date.ToString("G"), Tag = path });
                }
                var root = new DockPanel { Margin = new Thickness(15) };
                if (paths.Length == 0)
                {
                    root.Children.Add(new TextBlock { Text = "Предыдущих сохранённых версий пока нет." });
                    new Window { Owner=Window.GetWindow(this),Title="Последние изменения",Width=430,SizeToContent=SizeToContent.Height,Content=root,WindowStartupLocation=WindowStartupLocation.CenterOwner }.ShowDialog();
                    return;
                }
                var preview = new FlowDocumentScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
                var window = new Window { Owner=Window.GetWindow(this),Title="Последние изменения · "+note.Name,Width=670,Height=510,Content=root,WindowStartupLocation=WindowStartupLocation.CenterOwner };
                list.SelectionChanged += delegate
                {
                    var version = new FlowDocument();
                    library.ReadHistory((string)((ComboBoxItem)list.SelectedItem).Tag,version);
                    preview.Document = version;
                };
                Add(root,list,Dock.Top);
                Add(root,Button("Восстановить как новую версию",delegate
                {
                    if (MessageBox.Show(window,"Восстановить эту версию? Текущая останется в истории.","Последние изменения",MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
                    var restored = new FlowDocument();
                    library.ReadHistory((string)((ComboBoxItem)list.SelectedItem).Tag,restored);
                    var content = NoteDocument.Capture(restored);
                    library.Checkpoint(note,true);
                    editor.BeginChange();
                    try { NoteDocument.Restore(editor.Document,content); }
                    finally { editor.EndChange(); }
                    bodyDirty = true;
                    if (Save()) window.Close();
                }),Dock.Bottom);
                root.Children.Add(preview);
                list.SelectedIndex = 0;
                window.ShowDialog();
            });
        }
    }
}
