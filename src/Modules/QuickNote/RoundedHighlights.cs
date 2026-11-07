using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace ReFix.QuickNote
{
    // Kept behind the editor so text, selection and caret stay visible over the highlight.
    internal sealed class RoundedHighlights : FrameworkElement
    {
        readonly RichTextBox editor;
        readonly List<Run> colored = new List<Run>();
        ScrollContentPresenter viewport;
        bool dirty = true;
        internal bool ShowSymbols;
        internal IList<TextRange> SearchRanges = new List<TextRange>();

        internal RoundedHighlights(RichTextBox editor)
        {
            this.editor = editor;
            IsHitTestVisible = false;
            editor.TextChanged += delegate { dirty = true; InvalidateVisual(); };
            editor.SizeChanged += delegate { InvalidateVisual(); };
            editor.AddHandler(ScrollViewer.ScrollChangedEvent, new ScrollChangedEventHandler(delegate { InvalidateVisual(); }));
            editor.IsVisibleChanged += delegate { InvalidateVisual(); };
        }

        protected override void OnRender(DrawingContext context)
        {
            base.OnRender(context);
            if (!editor.IsVisible) return;
            if (dirty)
            {
                dirty = false;
                colored.Clear();
                foreach (var paragraph in NoteDocument.Paragraphs(editor.Document.Blocks))
                    foreach (var run in NoteDocument.Runs(paragraph.Inlines))
                    {
                        var brush = run.Background as SolidColorBrush;
                        if (brush != null && brush.Color.A != 0 && brush.Opacity == 0) colored.Add(run);
                    }
            }
            if (colored.Count == 0 && SearchRanges.Count == 0 && !ShowSymbols) return;
            viewport = viewport ?? Find(editor, item => item is ScrollContentPresenter) as ScrollContentPresenter;
            if (viewport == null) return;

            var clip = viewport.TransformToVisual(this).TransformBounds(new Rect(viewport.RenderSize));
            var transform = editor.TransformToVisual(this);
            context.PushClip(new RectangleGeometry(clip));
            Rect previous = Rect.Empty;
            Color previousColor = Colors.Transparent;
            var spans = new List<Tuple<TextPointer,TextPointer,Color>>();
            foreach (var run in colored)
            {
                var brush = run.Background as SolidColorBrush;
                if (brush != null && brush.Color.A != 0 && brush.Opacity == 0)
                    spans.Add(Tuple.Create(run.ContentStart, run.ContentEnd, brush.Color));
            }
            foreach (var range in SearchRanges)
                spans.Add(Tuple.Create(range.Start, range.End, Color.FromArgb(120, 35, 190, 84)));
            foreach (var span in spans)
            {
                var start = span.Item1;
                var end = span.Item2;
                if (!start.HasValidLayout || start.CompareTo(end) >= 0) continue;
                var first = transform.TransformBounds(start.GetCharacterRect(LogicalDirection.Forward));
                var last = transform.TransformBounds(end.GetCharacterRect(LogicalDirection.Backward));
                if (first.Top >= clip.Bottom || last.Bottom <= clip.Top) continue;
                var color = span.Item3;
                while (start.CompareTo(end) < 0)
                {
                    var next = start.GetLineStartPosition(1);
                    var stop = next != null && next.CompareTo(start) > 0 && next.CompareTo(end) < 0 ? next : end;
                    var a = transform.TransformBounds(start.GetCharacterRect(LogicalDirection.Forward));
                    var b = transform.TransformBounds(stop.GetCharacterRect(LogicalDirection.Backward));
                    if (a.Top >= clip.Bottom) break;
                    if (!a.IsEmpty && !b.IsEmpty && Math.Max(a.Bottom, b.Bottom) > clip.Top)
                    {
                        var rect = new Rect(new Point(Math.Min(a.Left, b.Left), Math.Min(a.Top, b.Top)),
                            new Point(Math.Max(a.Right, b.Right), Math.Max(a.Bottom, b.Bottom)));
                        if (rect.Width > 0 && rect.Height > 0)
                        {
                            if (!previous.IsEmpty && previousColor == color && Math.Abs(previous.Top - rect.Top) < 1 && previous.Right + 1 >= rect.Left && rect.Right >= previous.Left)
                                previous.Union(rect);
                            else
                            {
                                Draw(context, previous, previousColor);
                                previous = rect;
                                previousColor = color;
                            }
                        }
                    }
                    start = stop;
                }
            }
            Draw(context, previous, previousColor);
            if (ShowSymbols) DrawSymbols(context, transform, clip);
            context.Pop();
        }

        void DrawSymbols(DrawingContext context, GeneralTransform transform, Rect clip)
        {
            var brush = new SolidColorBrush(Color.FromRgb(110,145,160));
            foreach (var paragraph in NoteDocument.Paragraphs(editor.Document.Blocks))
            {
                if (!paragraph.ContentStart.HasValidLayout) continue;
                var top = transform.TransformBounds(paragraph.ContentStart.GetCharacterRect(LogicalDirection.Forward));
                var bottom = transform.TransformBounds(paragraph.ContentEnd.GetCharacterRect(LogicalDirection.Backward));
                if (top.Top >= clip.Bottom || bottom.Bottom <= clip.Top) continue;
                foreach (var run in NoteDocument.Runs(paragraph.Inlines))
                {
                    string value = run.Text;
                    for (int i = 0; i < value.Length; i++)
                    {
                        if (value[i] != ' ' && value[i] != '\t') continue;
                        var at = run.ContentStart.GetPositionAtOffset(i);
                        if (at != null) Symbol(context, transform.TransformBounds(at.GetCharacterRect(LogicalDirection.Forward)), value[i] == ' ' ? "·" : "→", brush, clip);
                    }
                }
                Symbol(context, bottom, "¶", brush, clip);
            }
        }
        static void Symbol(DrawingContext context, Rect rect, string text, Brush brush, Rect clip)
        {
            if (rect.IsEmpty || rect.Bottom < clip.Top || rect.Top > clip.Bottom) return;
            var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight, new Typeface("Segoe UI"), Math.Max(8, rect.Height * .68), brush, 1);
            context.DrawText(formatted, new Point(rect.Left, rect.Top + (rect.Height - formatted.Height) / 2));
        }

        static void Draw(DrawingContext context, Rect rectangle, Color color)
        {
            if (rectangle.IsEmpty) return;
            double radius = Math.Min(4, rectangle.Width / 2);
            context.DrawRoundedRectangle(new SolidColorBrush(color), null, rectangle, radius, radius);
        }

        static FrameworkElement Find(DependencyObject parent, Predicate<FrameworkElement> match)
        {
            var element = parent as FrameworkElement;
            if (element != null && match(element)) return element;
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = Find(VisualTreeHelper.GetChild(parent, i), match);
                if (child != null) return child;
            }
            return null;
        }
    }
}
