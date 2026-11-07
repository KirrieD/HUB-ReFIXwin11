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
            if (colored.Count == 0) return;
            viewport = viewport ?? Find(editor, item => item is ScrollContentPresenter) as ScrollContentPresenter;
            if (viewport == null) return;

            var clip = viewport.TransformToVisual(this).TransformBounds(new Rect(viewport.RenderSize));
            var transform = editor.TransformToVisual(this);
            context.PushClip(new RectangleGeometry(clip));
            Rect previous = Rect.Empty;
            Color previousColor = Colors.Transparent;
            foreach (var run in colored)
            {
                var start = run.ContentStart;
                var end = run.ContentEnd;
                if (!start.HasValidLayout || start.CompareTo(end) >= 0) continue;
                var first = transform.TransformBounds(start.GetCharacterRect(LogicalDirection.Forward));
                var last = transform.TransformBounds(end.GetCharacterRect(LogicalDirection.Backward));
                if (first.Top >= clip.Bottom || last.Bottom <= clip.Top) continue;
                var brush = run.Background as SolidColorBrush;
                if (brush == null || brush.Color.A == 0 || brush.Opacity != 0) continue;
                var color = brush.Color;
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
            context.Pop();
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
