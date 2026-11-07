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
        public string Foreground { get; set; }
        public string Background { get; set; }
    }

    internal static class NoteDocument
    {
        internal static List<NoteParagraph> Capture(FlowDocument document)
        {
            return document.Blocks.OfType<Paragraph>().Select(paragraph => new NoteParagraph
            {
                Size = paragraph.FontSize, Heading = paragraph.FontWeight >= FontWeights.SemiBold,
                Parts = CaptureInlines(paragraph.Inlines).ToList()
            }).ToList();
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
            var paragraphs = new List<Paragraph>();
            foreach (var record in records)
            {
                var paragraph = new Paragraph { FontSize = FontSize(record.Size), FontWeight = record.Heading ? FontWeights.SemiBold : FontWeights.Normal };
                foreach (var part in record.Parts)
                {
                    if (part.Break) paragraph.Inlines.Add(new LineBreak());
                    else if (part.Image != null) paragraph.Inlines.Add(new InlineUIContainer(DecodeImage(Convert.FromBase64String(part.Image))));
                    else
                    {
                        var run = new Run(part.Text ?? "") { FontSize = FontSize(part.Size), FontWeight = FontWeight.FromOpenTypeWeight(Math.Max(1, Math.Min(999, part.Weight))), FontStyle = part.Italic ? FontStyles.Italic : FontStyles.Normal };
                        if (part.Underline) run.TextDecorations = TextDecorations.Underline;
                        if (part.Foreground != null) run.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(part.Foreground));
                        if (part.Background != null) run.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(part.Background));
                        paragraph.Inlines.Add(run);
                    }
                }
                paragraphs.Add(paragraph);
            }
            document.Blocks.Clear();
            foreach (var paragraph in paragraphs) document.Blocks.Add(paragraph);
        }

        static double FontSize(double value) { return value >= 6 && value <= 120 ? value : 15; }
    }
}
