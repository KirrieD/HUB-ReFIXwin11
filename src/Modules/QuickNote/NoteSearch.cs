using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Documents;

namespace ReFix.QuickNote
{
    internal static class NoteSearch
    {
        internal static List<TextRange> Find(FlowDocument document, string query, bool exactCase)
        {
            var matches = new List<TextRange>();
            if (String.IsNullOrEmpty(query)) return matches;
            var comparison = exactCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
            foreach (var paragraph in NoteDocument.Paragraphs(document.Blocks))
            {
                var text = new StringBuilder();
                var slices = new List<Tuple<int,TextPointer,int>>();
                for (var pointer = paragraph.ContentStart; pointer != null && pointer.CompareTo(paragraph.ContentEnd) < 0; )
                {
                    var context = pointer.GetPointerContext(LogicalDirection.Forward);
                    if (context == TextPointerContext.Text)
                    {
                        string part = pointer.GetTextInRun(LogicalDirection.Forward);
                        slices.Add(Tuple.Create(text.Length, pointer, part.Length));
                        text.Append(part);
                    }
                    else if (context == TextPointerContext.EmbeddedElement || (context == TextPointerContext.ElementStart && pointer.GetAdjacentElement(LogicalDirection.Forward) is LineBreak))
                        text.Append('\uFFFC');
                    pointer = pointer.GetNextContextPosition(LogicalDirection.Forward);
                }
                string body = text.ToString();
                int at = 0;
                while ((at = body.IndexOf(query, at, comparison)) >= 0)
                {
                    var start = Position(slices, at, false);
                    var end = Position(slices, at + query.Length, true);
                    if (start != null && end != null) matches.Add(new TextRange(start,end));
                    at += query.Length;
                }
            }
            return matches;
        }

        static TextPointer Position(List<Tuple<int,TextPointer,int>> slices, int index, bool end)
        {
            int lo = 0, hi = slices.Count - 1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                var slice = slices[mid];
                int offset = index - slice.Item1;
                if (offset < 0 || (end && offset == 0)) hi = mid - 1;
                else if (offset > slice.Item3 || (!end && offset == slice.Item3)) lo = mid + 1;
                else return slice.Item2.GetPositionAtOffset(offset, end ? LogicalDirection.Backward : LogicalDirection.Forward);
            }
            return null;
        }

        internal static IEnumerable<TextPointer> Lines(FlowDocument document)
        {
            foreach (var paragraph in NoteDocument.Paragraphs(document.Blocks))
            {
                yield return paragraph.ContentStart;
                for (var pointer = paragraph.ContentStart; pointer != null && pointer.CompareTo(paragraph.ContentEnd) < 0; pointer = pointer.GetNextContextPosition(LogicalDirection.Forward))
                    if (pointer.GetPointerContext(LogicalDirection.Forward) == TextPointerContext.ElementStart && pointer.GetAdjacentElement(LogicalDirection.Forward) is LineBreak)
                        yield return ((LineBreak)pointer.GetAdjacentElement(LogicalDirection.Forward)).ElementEnd;
            }
        }
    }
}
