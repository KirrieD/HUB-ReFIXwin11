using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Xml.Linq;

namespace ReFix.QuickNote
{
    internal sealed partial class NoteEditor
    {
        void Export()
        {
            if (!Save()) return;
            string suggested = new string(note.Name.Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray()).Trim('.', ' ');
            var dialog = new Microsoft.Win32.SaveFileDialog
            {
                FileName = String.IsNullOrEmpty(suggested) ? "Заметка" : suggested,
                Filter = "Документ Word (*.docx)|*.docx|Текст UTF-8, без изображений (*.txt)|*.txt|HTML, изображения внутри (*.html)|*.html|Markdown, изображения рядом (*.md)|*.md",
                FilterIndex = note.Markdown ? 4 : 1, AddExtension = true, OverwritePrompt = true
            };
            if (dialog.ShowDialog(Window.GetWindow(this)) != true) return;
            string extension = Path.GetExtension(dialog.FileName).ToLowerInvariant();
            string assets = Path.Combine(Path.GetDirectoryName(dialog.FileName), Path.GetFileNameWithoutExtension(dialog.FileName) + "_images");
            string help;
            switch (extension)
            {
                case ".txt": help = "Формат TXT сохраняет только текст. Изображения и оформление остаются в заметке."; break;
                case ".md": help = "Изображения будут сохранены в папке:\n" + assets + "\nMarkdown сохраняет базовое форматирование. Цвета и выравнивание остаются в заметке."; break;
                case ".docx":
                case ".html": help = "Текст, оформление и изображения будут сохранены внутри файла:\n" + dialog.FileName; break;
                default: throw new InvalidOperationException("Выберите формат DOCX, TXT, HTML или MD.");
            }
            if (MessageBox.Show(Window.GetWindow(this), help, "Сохранить как", MessageBoxButton.OKCancel, MessageBoxImage.Information) != MessageBoxResult.OK) return;
            if (extension == ".md" && note.Markdown)
            {
                string markdown = MarkdownNote.LocalImage.Replace(PlainText(), match =>
                {
                    string id = match.Groups[2].Value;
                    byte[] bytes = library.GetImage(note,id);
                    if (bytes == null) return match.Value;
                    Directory.CreateDirectory(assets);
                    string target = Path.Combine(assets,id);
                    if (File.Exists(target) && !File.ReadAllBytes(target).SequenceEqual(bytes))
                        target = Path.Combine(assets,Guid.NewGuid().ToString("N") + Path.GetExtension(id));
                    if (!File.Exists(target)) File.WriteAllBytes(target,bytes);
                    return "![" + match.Groups[1].Value + "](" + Uri.EscapeDataString(Path.GetFileName(assets)) + "/" + Uri.EscapeDataString(Path.GetFileName(target)) + ")";
                });
                ExportFile.WriteText(dialog.FileName, IncludeHints(markdown));
                return;
            }
            var document = OutputDocument();
            if (extension == ".txt") ExportFile.WriteText(dialog.FileName, new TextRange(document.ContentStart,document.ContentEnd).Text.TrimEnd('\r','\n'));
            else ExportFile.Write(dialog.FileName, extension, NoteDocument.Capture(document), assets);
        }

        string IncludeHints(string body)
        {
            return String.Join(Environment.NewLine + Environment.NewLine, new[] { note.TopHint, body, note.BottomHint }.Where(t => !String.IsNullOrWhiteSpace(t)));
        }

        FlowDocument OutputDocument()
        {
            var document = note.Markdown ? MarkdownNote.Render(PlainText(), name => library.GetImage(note,name)) : new FlowDocument();
            if (!note.Markdown) NoteDocument.Restore(document, NoteDocument.Capture(editor.Document));
            if (!String.IsNullOrWhiteSpace(note.TopHint))
            {
                var hint = new Paragraph(new Run(note.TopHint)) { FontStyle = FontStyles.Italic };
                if (document.Blocks.FirstBlock == null) document.Blocks.Add(hint);
                else document.Blocks.InsertBefore(document.Blocks.FirstBlock,hint);
            }
            if (!String.IsNullOrWhiteSpace(note.BottomHint)) document.Blocks.Add(new Paragraph(new Run(note.BottomHint)) { FontStyle = FontStyles.Italic });
            return document;
        }
    }

    internal static class ExportFile
    {
        internal static void WriteText(string path, string content)
        {
            Atomic(path, stream => { using (var writer = new StreamWriter(stream, new UTF8Encoding(false), 1024, true)) writer.Write(content); });
        }

        internal static void Write(string path, string extension, List<NoteParagraph> blocks, string assets)
        {
            if (extension == ".docx") { Atomic(path, stream => new WordExport().Write(stream,blocks)); return; }
            if (extension == ".html")
            {
                var html = new StringBuilder("<!doctype html><html lang=\"ru\"><meta charset=\"utf-8\"><title>Заметка</title><body>");
                Html(html, blocks);
                html.Append("</body></html>");
                WriteText(path,html.ToString());
                return;
            }
            var markdown = new StringBuilder();
            Markdown(markdown, blocks, assets, 0, "");
            WriteText(path,markdown.ToString());
        }

        static void Atomic(string path, Action<Stream> write)
        {
            string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary,FileMode.CreateNew,FileAccess.Write)) write(stream);
                if (File.Exists(path)) File.Replace(temporary,path,null);
                else File.Move(temporary,path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        internal static bool Ordered(string marker)
        {
            return marker == "Decimal" || marker == "LowerLatin" || marker == "UpperLatin" || marker == "LowerRoman" || marker == "UpperRoman";
        }
        internal static string Hex(string value)
        {
            string hex = (value ?? "").TrimStart('#');
            if (hex.Length == 8) hex = hex.Substring(2);
            return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? hex : null;
        }
        internal static byte[] Png(string encoded, out double width, out double height)
        {
            var bitmap = (BitmapSource)NoteDocument.DecodeImage(Convert.FromBase64String(encoded)).Source;
            width = Math.Min(520,bitmap.Width);
            height = bitmap.Height * width / bitmap.Width;
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var bytes = new MemoryStream()) { encoder.Save(bytes); return bytes.ToArray(); }
        }
        static void Html(StringBuilder html, IEnumerable<NoteParagraph> blocks)
        {
            foreach (var block in blocks)
            {
                if (block.Items != null)
                {
                    string tag = Ordered(block.Marker) ? "ol" : "ul";
                    html.Append("<").Append(tag);
                    if (tag == "ol") html.Append(" start=\"").Append(Math.Max(1,block.StartIndex)).Append("\"");
                    html.Append(">");
                    foreach (var item in block.Items) { html.Append("<li>"); Html(html,item); html.Append("</li>"); }
                    html.Append("</").Append(tag).Append(">");
                    continue;
                }
                html.Append("<p style=\"white-space:pre-wrap;text-align:").Append(Alignment(block.Alignment)).Append("\">");
                foreach (var part in block.Parts)
                {
                    if (part.Break) html.Append("<br>");
                    else if (part.Image != null)
                    {
                        double w,h; var png = Png(part.Image,out w,out h);
                        html.Append("<img alt=\"Изображение\" style=\"max-width:100%\" width=\"").Append((int)w).Append("\" src=\"data:image/png;base64,").Append(Convert.ToBase64String(png)).Append("\">");
                    }
                    else
                    {
                        string font = WebUtility.HtmlEncode((part.Font ?? "Segoe UI").Replace("'","").Replace(";","").Replace("\\",""));
                        html.Append("<span style=\"font-family:'").Append(font).Append("';font-size:").Append(part.Size.ToString("0.##",CultureInfo.InvariantCulture)).Append("px;");
                        if (part.Weight >= 600) html.Append("font-weight:bold;");
                        if (part.Italic) html.Append("font-style:italic;");
                        if (part.Underline || part.Strike) html.Append("text-decoration:").Append(part.Underline ? "underline " : "").Append(part.Strike ? "line-through" : "").Append(";");
                        if (Hex(part.Foreground) != null) html.Append("color:#").Append(Hex(part.Foreground)).Append(";");
                        if (Hex(part.Background) != null && !(part.Background ?? "").StartsWith("#00",StringComparison.Ordinal)) html.Append("background-color:#").Append(Hex(part.Background)).Append(";");
                        html.Append("\">").Append(WebUtility.HtmlEncode(part.Text)).Append("</span>");
                    }
                }
                html.Append("</p>");
            }
        }

        static string Alignment(string value)
        {
            return new[] { "Center","Right","Justify" }.Contains(value) ? value.ToLowerInvariant() : "left";
        }

        static void Markdown(StringBuilder text, IEnumerable<NoteParagraph> blocks, string assets, int depth, string prefix)
        {
            foreach (var block in blocks)
            {
                if (block.Items != null)
                {
                    int n = Math.Max(1,block.StartIndex);
                    foreach (var item in block.Items) Markdown(text,item,assets,depth+1,new string(' ',depth*4)+(Ordered(block.Marker) ? (n++).ToString()+". " : "- "));
                    continue;
                }
                text.Append(prefix);
                if (block.Heading) text.Append(block.Size >= 28 ? "# " : block.Size >= 23 ? "## " : "### ");
                foreach (var part in block.Parts)
                {
                    if (part.Break) text.Append("  ").AppendLine();
                    else if (part.Image != null)
                    {
                        double w,h; var bytes = Png(part.Image,out w,out h);
                        Directory.CreateDirectory(assets);
                        string name = "image-" + Guid.NewGuid().ToString("N") + ".png";
                        File.WriteAllBytes(Path.Combine(assets,name),bytes);
                        text.Append("![Изображение](").Append(Uri.EscapeDataString(Path.GetFileName(assets))).Append("/").Append(name).Append(")");
                    }
                    else
                    {
                        string value = System.Text.RegularExpressions.Regex.Replace(part.Text ?? "", @"([\\\x60*_{}\[\]<>#+.!|~-])", @"\$1");
                        if (part.Strike) value = "~~" + value + "~~";
                        if (part.Italic) value = "*" + value + "*";
                        if (part.Weight >= 600 && !block.Heading) value = "**" + value + "**";
                        text.Append(value);
                    }
                }
                text.AppendLine();
                if (depth == 0) text.AppendLine();
                prefix = new string(' ',depth*4);
            }
        }
    }
}
