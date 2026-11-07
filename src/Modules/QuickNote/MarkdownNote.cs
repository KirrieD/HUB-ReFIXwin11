using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ReFix.QuickNote
{
    internal static class MarkdownNote
    {
        internal static readonly Regex LocalImage = new Regex(@"!\[([^\]\r\n]*)\]\(refix-image:([a-f0-9]{32}\.[a-z0-9]{1,5})\)");
        static readonly Regex Inline = new Regex(@"!\[([^\]]*)\]\(refix-image:([a-f0-9]{32}\.[a-z0-9]{1,5})\)|\*\*(.+?)\*\*|__(.+?)__|~~(.+?)~~|\x60([^\x60]+)\x60|\*([^*]+)\*|_([^_]+)_|\[([^\]]+)\]\(([^)]+)\)");
        static readonly Regex Heading = new Regex(@"^(#{1,6})\s+(.+)$");
        static readonly Regex Bullet = new Regex(@"^(\s*)([-+*]|\d+\.)\s+(.+)$");

        internal static FlowDocument Render(string source, Func<string,byte[]> image)
        {
            var document = new FlowDocument { FontSize = 15 };
            bool code = false;
            System.Windows.Documents.List list = null;
            foreach (string line in source.Replace("\r\n", "\n").Split('\n'))
            {
                if (line.TrimStart().StartsWith(new string((char)96, 3), StringComparison.Ordinal)) { code = !code; list = null; continue; }
                var paragraph = new Paragraph { Margin = new Thickness(0,0,0,5) };
                if (code) { paragraph.Inlines.Add(new Run(line) { FontFamily = new FontFamily("Consolas") }); document.Blocks.Add(paragraph); continue; }
                var heading = Heading.Match(line);
                var bullet = Bullet.Match(line);
                if (heading.Success)
                {
                    paragraph.FontSize = 31 - heading.Groups[1].Length * 2;
                    paragraph.FontWeight = FontWeights.SemiBold;
                    AddInlines(paragraph, heading.Groups[2].Value, image);
                }
                else if (bullet.Success)
                {
                    bool ordered = Char.IsDigit(bullet.Groups[2].Value[0]);
                    var marker = ordered ? TextMarkerStyle.Decimal : TextMarkerStyle.Disc;
                    if (list == null || list.MarkerStyle != marker)
                    {
                        list = new System.Windows.Documents.List { MarkerStyle = marker };
                        int start;
                        if (ordered && Int32.TryParse(bullet.Groups[2].Value.TrimEnd('.'), out start)) list.StartIndex = Math.Max(1,start);
                        document.Blocks.Add(list);
                    }
                    AddInlines(paragraph, bullet.Groups[3].Value, image);
                    list.ListItems.Add(new ListItem(paragraph));
                    continue;
                }
                else
                {
                    string value = line;
                    if (value.StartsWith("> ", StringComparison.Ordinal))
                    {
                        value = value.Substring(2);
                        paragraph.FontStyle = FontStyles.Italic;
                        paragraph.Padding = new Thickness(12,0,0,0);
                    }
                    AddInlines(paragraph,value,image);
                }
                list = null;
                document.Blocks.Add(paragraph);
            }
            return document;
        }

        static void AddInlines(Paragraph paragraph, string text, Func<string,byte[]> image)
        {
            int cursor = 0;
            foreach (Match match in Inline.Matches(text))
            {
                if (match.Index > cursor) paragraph.Inlines.Add(new Run(text.Substring(cursor,match.Index-cursor)));
                if (match.Groups[2].Success)
                {
                    var bytes = image(match.Groups[2].Value);
                    if (bytes != null) paragraph.Inlines.Add(new InlineUIContainer(NoteDocument.DecodeImage(bytes)));
                    else paragraph.Inlines.Add(new Run(match.Groups[1].Value));
                }
                else if (match.Groups[3].Success || match.Groups[4].Success)
                    paragraph.Inlines.Add(new Bold(new Run(match.Groups[3].Success ? match.Groups[3].Value : match.Groups[4].Value)));
                else if (match.Groups[5].Success)
                    paragraph.Inlines.Add(new Run(match.Groups[5].Value) { TextDecorations = TextDecorations.Strikethrough });
                else if (match.Groups[6].Success)
                    paragraph.Inlines.Add(new Run(match.Groups[6].Value) { FontFamily = new FontFamily("Consolas") });
                else if (match.Groups[7].Success || match.Groups[8].Success)
                    paragraph.Inlines.Add(new Italic(new Run(match.Groups[7].Success ? match.Groups[7].Value : match.Groups[8].Value)));
                else
                    paragraph.Inlines.Add(new Run(match.Groups[9].Value + " (" + match.Groups[10].Value + ")") { TextDecorations = TextDecorations.Underline });
                cursor = match.Index + match.Length;
            }
            if (cursor < text.Length) paragraph.Inlines.Add(new Run(text.Substring(cursor)));
        }
    }

    internal sealed partial class NoteEditor
    {
        void PreviewMarkdown()
        {
            var view = new FlowDocumentScrollViewer { Document = MarkdownNote.Render(PlainText(), name => library.GetImage(note,name)), VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            new Window { Owner = Window.GetWindow(this), Title = "Предпросмотр Markdown · " + note.Name, Width = 750, Height = 570, Content = view, WindowStartupLocation = WindowStartupLocation.CenterOwner }.ShowDialog();
        }

        void InsertMarkdownImage()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.ico|Все файлы|*.*" };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) AddMarkdownImage(dialog.FileName);
        }

        void AddMarkdownImage(string path)
        {
            var bytes = File.ReadAllBytes(path);
            NoteDocument.DecodeImage(bytes);
            string name = library.AddImage(note, bytes, Path.GetExtension(path));
            InsertText("![" + Path.GetFileNameWithoutExtension(path).Replace("]","") + "](refix-image:" + name + ")");
        }
    }
}
