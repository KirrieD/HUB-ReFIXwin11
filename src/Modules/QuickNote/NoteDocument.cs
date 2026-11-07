using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReFix.QuickNote
{
    public sealed class NoteParagraph
    {
        public string Marker { get; set; }
        public int StartIndex { get; set; }
        public List<List<NoteParagraph>> Items { get; set; }
        public double Size { get; set; }
        public bool Heading { get; set; }
        public List<NotePart> Parts { get; set; }
    }

    public sealed class NotePart
    {
        public string Text { get; set; }
        public bool Break { get; set; }
        public string Image { get; set; }
        public double Size { get; set; }
        public int Weight { get; set; }
        public bool Italic { get; set; }
        public bool Underline { get; set; }
        public bool Strike { get; set; }
        public string Foreground { get; set; }
        public string Background { get; set; }
    }

    internal static class NoteDocument
    {
        internal static List<NoteParagraph> Capture(FlowDocument document)
        {
            return CaptureBlocks(document.Blocks);
        }

        static List<NoteParagraph> CaptureBlocks(BlockCollection blocks)
        {
            var result = new List<NoteParagraph>();
            foreach (var block in blocks)
            {
                var paragraph = block as Paragraph;
                if (paragraph != null)
                    result.Add(new NoteParagraph { Size = paragraph.FontSize, Heading = paragraph.FontWeight >= FontWeights.SemiBold, Parts = CaptureInlines(paragraph.Inlines).ToList() });
                var list = block as System.Windows.Documents.List;
                if (list != null)
                    result.Add(new NoteParagraph { Marker = list.MarkerStyle.ToString(), StartIndex = list.StartIndex, Items = list.ListItems.Select(item => CaptureBlocks(item.Blocks)).ToList() });
            }
            return result;
        }

        internal static IEnumerable<Paragraph> Paragraphs(BlockCollection blocks)
        {
            foreach (var block in blocks)
            {
                if (block is Paragraph) yield return (Paragraph)block;
                var list = block as System.Windows.Documents.List;
                if (list != null)
                    foreach (var item in list.ListItems)
                        foreach (var paragraph in Paragraphs(item.Blocks)) yield return paragraph;
            }
        }

        internal static IEnumerable<Run> Runs(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                if (inline is Run) yield return (Run)inline;
                var span = inline as Span;
                if (span != null)
                    foreach (var run in Runs(span.Inlines)) yield return run;
            }
        }

        internal static SolidColorBrush Highlight(Color color)
        {
            var brush = new SolidColorBrush(color) { Opacity = 0 };
            brush.Freeze();
            return brush;
        }

        static IEnumerable<NotePart> CaptureInlines(InlineCollection inlines)
        {
            foreach (var inline in inlines)
            {
                var span = inline as Span;
                if (span != null)
                {
                    foreach (var part in CaptureInlines(span.Inlines)) yield return part;
                    continue;
                }

                var run = inline as Run;
                if (run != null)
                {
                    yield return new NotePart
                    {
                        Text = run.Text, Size = run.FontSize, Weight = run.FontWeight.ToOpenTypeWeight(),
                        Italic = run.FontStyle != FontStyles.Normal,
                        Underline = run.TextDecorations != null && run.TextDecorations.Any(d => d.Location == TextDecorationLocation.Underline),
                        Strike = run.TextDecorations != null && run.TextDecorations.Any(d => d.Location == TextDecorationLocation.Strikethrough),
                        Foreground = LocalColor(run, TextElement.ForegroundProperty),
                        Background = LocalColor(run, TextElement.BackgroundProperty)
                    };
                    continue;
                }
                if (inline is LineBreak) { yield return new NotePart { Break = true }; continue; }
                var container = inline as InlineUIContainer;
                var image = container == null ? null : container.Child as Image;
                if (image == null || !(image.Source is BitmapSource)) continue;
                var original = image.Tag as byte[];
                if (original == null)
                {
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create((BitmapSource)image.Source));
                    using (var stream = new MemoryStream()) { encoder.Save(stream); original = stream.ToArray(); }
                }
                yield return new NotePart { Image = Convert.ToBase64String(original) };
            }
        }

        static string LocalColor(TextElement element, DependencyProperty property)
        {
            for (var current = element; current != null; current = current.Parent as TextElement)
            {
                var brush = current.ReadLocalValue(property) as SolidColorBrush;
                if (brush != null) return brush.Color.ToString();
            }
            return null;
        }

        internal static Image DecodeImage(byte[] original)
        {
            using (var input = new MemoryStream(original, false))
            {
                var bitmap = BitmapDecoder.Create(input, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
                return new Image { Source = bitmap, Tag = original, Width = Math.Min(520, bitmap.Width), Stretch = Stretch.Uniform };
            }
        }

        internal static void Restore(FlowDocument document, IEnumerable<NoteParagraph> records)
        {
            var blocks = RestoreBlocks(records).ToList();
            document.Blocks.Clear();
            foreach (var block in blocks) document.Blocks.Add(block);
        }

        static IEnumerable<Block> RestoreBlocks(IEnumerable<NoteParagraph> records)
        {
            foreach (var record in records)
            {
                if (record.Items != null)
                {
                    TextMarkerStyle marker;
                    if (!Enum.TryParse(record.Marker, out marker) || !Enum.IsDefined(typeof(TextMarkerStyle), marker)) marker = TextMarkerStyle.Disc;
                    var list = new System.Windows.Documents.List { MarkerStyle = marker, StartIndex = Math.Max(1, record.StartIndex) };
                    foreach (var item in record.Items)
                    {
                        var listItem = new ListItem();
                        foreach (var block in RestoreBlocks(item)) listItem.Blocks.Add(block);
                        list.ListItems.Add(listItem);
                    }
                    yield return list;
                    continue;
                }
                var paragraph = new Paragraph { FontSize = FontSize(record.Size), FontWeight = record.Heading ? FontWeights.SemiBold : FontWeights.Normal };
                foreach (var part in record.Parts)
                {
                    if (part.Break) paragraph.Inlines.Add(new LineBreak());
                    else if (part.Image != null) paragraph.Inlines.Add(new InlineUIContainer(DecodeImage(Convert.FromBase64String(part.Image))));
                    else
                    {
                        var run = new Run(part.Text ?? "") { FontSize = FontSize(part.Size), FontWeight = FontWeight.FromOpenTypeWeight(Math.Max(1, Math.Min(999, part.Weight))), FontStyle = part.Italic ? FontStyles.Italic : FontStyles.Normal };
                        if (part.Underline || part.Strike)
                        {
                            var decorations = new TextDecorationCollection();
                            if (part.Underline) decorations.Add(TextDecorations.Underline[0]);
                            if (part.Strike) decorations.Add(TextDecorations.Strikethrough[0]);
                            run.TextDecorations = decorations;
                        }
                        if (part.Foreground != null) run.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(part.Foreground));
                        if (part.Background != null) run.Background = Highlight((Color)ColorConverter.ConvertFromString(part.Background));
                        paragraph.Inlines.Add(run);
                    }
                }
                yield return paragraph;
            }

        }

        static double FontSize(double value) { return value >= 6 && value <= 120 ? value : 15; }
    }
}
