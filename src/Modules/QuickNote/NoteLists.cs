using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace ReFix.QuickNote
{
    internal sealed partial class NoteEditor
    {
        static readonly int[] IndentSteps = { 12, 18, 24, 30, 36, 48, 60, 72 };
        const int DefaultIndentStep = 24;
        const int MaximumIndentStep = 120;
        internal const int MaximumFirstItemIndent = 720;
        int IndentStep { get { return note.IndentStep>0&&note.IndentStep<=MaximumIndentStep?note.IndentStep:DefaultIndentStep; } }

        void ConfigureListInput()
        {
            ApplyListSpacing(editor.Document.Blocks);
            editor.PreviewTextInput += delegate(object sender, TextCompositionEventArgs e)
            {
                if (e.Text == " " && TryStartList()) e.Handled = true;
            };
            editor.PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key != Key.Tab || CurrentList() == null) return;
                var modifiers = Keyboard.Modifiers;
                if (modifiers != ModifierKeys.None && modifiers != ModifierKeys.Shift) return;
                ChangeIndent(modifiers == ModifierKeys.None);
                e.Handled = true;
            };
        }

        void AddIndentTools(Panel indentGroup)
        {
            indentGroup.Children.Add(FormatButton("⇤", "Уменьшить отступ", () => ChangeIndent(false), FontWeights.Normal, FontStyles.Normal, null));
            indentGroup.Children.Add(FormatButton("⇥", "Увеличить отступ", () => ChangeIndent(true), FontWeights.Normal, FontStyles.Normal, null));
            const string hint = "Шаг отступа от 1 до 120. Нажмите Enter для применения.";
            var group=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,2,6,2) };
            group.Children.Add(new TextBlock {Text="Шаг",VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,5,0)});
            var field=Field(IndentStep.ToString(),3);
            field.Width=44;field.TextAlignment=TextAlignment.Center;field.ToolTip=hint;
            System.Windows.Automation.AutomationProperties.SetName(field,"Шаг отступа");
            group.Children.Add(field);
            var presets=new ComboBox { MinWidth=85,ToolTip="Готовые шаги отступа",Margin=new Thickness(4,0,0,0) };
            System.Windows.Automation.AutomationProperties.SetName(presets,"Готовые шаги отступа");
            presets.Items.Add("Готовые");
            foreach(int value in IndentSteps)presets.Items.Add(value);
            presets.SelectedIndex=0;
            presets.SelectionChanged+=delegate
            {
                if(!(presets.SelectedItem is int))return;
                var range=FormattingRange();
                TrySetIndentStep(presets.SelectedItem.ToString());
                field.Text=IndentStep.ToString();
                presets.SelectedIndex=0;
                FocusEditorAt(range);
            };
            group.Children.Add(presets);
            field.PreviewKeyDown+=delegate(object sender,KeyEventArgs e)
            {
                if(e.Key!=Key.Enter)return;
                e.Handled=true;
                var range=FormattingRange();
                if(TrySetIndentStep(field.Text))FocusEditorAt(range);
                else {status.Text=hint;field.SelectAll();}
            };
            field.LostKeyboardFocus+=delegate
            {
                if(!TrySetIndentStep(field.Text))status.Text=hint;
                field.Text=IndentStep.ToString();
            };
            indentGroup.Children.Add(group);
        }

        bool TrySetIndentStep(string input)
        {
            int value;
            if(!Int32.TryParse(input,out value)||value<1||value>MaximumIndentStep)return false;
            int previous=IndentStep;
            if(value==previous)return true;
            note.IndentStep=value;
            ApplyListSpacing(editor.Document.Blocks,previous);
            bodyDirty=true;
            metadataDirty=true;
            QueueSave();
            return true;
        }

        bool TryStartList()
        {
            if (!editor.Selection.IsEmpty || CurrentList() != null) return false;
            var caret = editor.CaretPosition;
            var paragraph = caret.Paragraph;
            if (paragraph == null || new TextRange(caret, paragraph.ContentEnd).Text.Length != 0) return false;
            var prefix = new TextRange(paragraph.ContentStart, caret);
            string text = prefix.Text;
            int start = 1;
            RoutedUICommand command;
            if (text == "-" || text == "*") command = EditingCommands.ToggleBullets;
            else if (text.Length >= 2 && text.Length <= 10 && text.EndsWith(".", StringComparison.Ordinal) &&
                text.Take(text.Length - 1).All(c => c >= '0' && c <= '9') &&
                Int32.TryParse(text.Substring(0, text.Length - 1), out start) && start > 0)
                command = EditingCommands.ToggleNumbering;
            else return false;
            if (!command.CanExecute(null, editor)) return false;

            editor.BeginChange();
            try
            {
                // Turn the paragraph into a list before removing its marker so a failed command leaves the text intact.
                command.Execute(null, editor);
                var list = CurrentList();
                if (list == null) return false;
                paragraph = editor.CaretPosition.Paragraph;
                new TextRange(paragraph.ContentStart, paragraph.ContentEnd).Text = "";
                editor.CaretPosition = paragraph.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
                if (command == EditingCommands.ToggleNumbering && list.ListItems.Count == 1) list.StartIndex = start;
                ApplyListSpacing(editor.Document.Blocks);
            }
            finally { editor.EndChange(); }
            return true;
        }

        List CurrentList()
        {
            var paragraph = editor.Selection.Start.Paragraph;
            var item = paragraph == null ? null : paragraph.Parent as ListItem;
            return item == null ? null : item.Parent as List;
        }

        void ToggleList(RoutedUICommand command)
        {
            editor.BeginChange();
            try
            {
                command.Execute(null, editor);
                ApplyListSpacing(editor.Document.Blocks);
            }
            finally { editor.EndChange(); }
        }

        void ChangeIndent(bool increase)
        {
            editor.BeginChange();
            try
            {
                if (CurrentList() != null)
                {
                    var item = editor.Selection.Start.Paragraph.Parent as ListItem;
                    var list = item.List;
                    double firstItemIndent = Math.Max(0, LeftIndent(item.Margin));
                    if (editor.Selection.IsEmpty && ((increase && item.PreviousListItem == null) || (!increase && firstItemIndent > 0)))
                    {
                        // A first item has no preceding sibling to act as the parent of a nested list.
                        var margin = item.Margin;
                        margin.Left = Math.Max(0, Math.Min(Math.Min(MaximumFirstItemIndent,10*IndentStep), firstItemIndent + (increase ? IndentStep : -IndentStep)));
                        item.Margin = margin;
                    }
                    else if (increase || list.Parent is ListItem)
                    {
                        var command = increase ? EditingCommands.IncreaseIndentation : EditingCommands.DecreaseIndentation;
                        command.Execute(null, editor);
                    }
                    ApplyListSpacing(editor.Document.Blocks);
                }
                else
                {
                    var start = editor.Selection.Start.Paragraph;
                    var end = editor.Selection.End.Paragraph;
                    foreach (var paragraph in NoteDocument.Paragraphs(editor.Document.Blocks).SkipWhile(p => p != start))
                    {
                        var margin = paragraph.Margin;
                        var left = LeftIndent(margin);
                        margin.Left = Math.Max(0, Math.Min(480, left + (increase ? IndentStep : -IndentStep)));
                        paragraph.Margin = margin;
                        if (paragraph == end) break;
                    }
                }
            }
            finally { editor.EndChange(); }
            bodyDirty = true;
            QueueSave();
        }

        static double LeftIndent(Thickness margin) { return Double.IsNaN(margin.Left) ? 0 : margin.Left; }

        void ApplyListSpacing(BlockCollection blocks, int previousStep = 0)
        {
            foreach (var list in blocks.OfType<List>())
            {
                var padding = list.Padding;
                padding.Left = IndentStep;
                if (list.Padding != padding) list.Padding = padding;
                foreach (var item in list.ListItems)
                {
                    if (previousStep > 0 && previousStep != IndentStep && LeftIndent(item.Margin) > 0)
                    {
                        var margin = item.Margin;
                        margin.Left = Math.Min(Math.Min(MaximumFirstItemIndent,10*IndentStep), margin.Left / previousStep * IndentStep);
                        item.Margin = margin;
                    }
                    ApplyListSpacing(item.Blocks, previousStep);
                }
            }
        }
    }
}
