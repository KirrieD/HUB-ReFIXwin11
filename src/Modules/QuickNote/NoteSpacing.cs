using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;

namespace ReFix.QuickNote
{
    internal sealed partial class NoteEditor
    {
        void AddSpacingTools(Panel group)
        {
            AddSpacingField(group,"До","Интервал перед абзацем",true);
            AddSpacingField(group,"После","Интервал после абзаца",false);
        }

        void AddSpacingField(Panel group,string label,string name,bool before)
        {
            var pair=new StackPanel { Orientation=Orientation.Horizontal,Margin=new Thickness(0,2,6,2) };
            pair.Children.Add(new TextBlock { Text=label,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,0,4,0) });
            var field=Field("",4);
            field.Width=53;field.TextAlignment=TextAlignment.Center;
            field.ToolTip=name+" · 0–120. Пустое поле возвращает автоматический интервал.";
            System.Windows.Automation.AutomationProperties.SetName(field,name);
            pair.Children.Add(field);group.Children.Add(pair);

            TextRange target=null;
            string initial=null;
            Action refresh=delegate
            {
                TextRange selection=new TextRange(editor.Selection.Start,editor.Selection.End);
                var values=SpacingParagraphs(selection).Select(p=>before?p.Margin.Top:p.Margin.Bottom).Distinct().Take(2).ToArray();
                field.Text=values.Length>1?"—":values.Length==0||Double.IsNaN(values[0])?"Авто":values[0].ToString("0.##",CultureInfo.CurrentCulture);
            };
            Func<bool> commit=delegate
            {
                if(target==null||field.Text==initial)return true;
                string text=field.Text.Trim();
                double amount=Double.NaN;
                if(text.Length>0&&!String.Equals(text,"Авто",StringComparison.CurrentCultureIgnoreCase))
                {
                    int value;
                    if(!Int32.TryParse(text,out value)||value<0||value>NoteDocument.MaximumParagraphSpacing)
                    {
                        status.Text="Введите интервал от 0 до 120.";
                        return false;
                    }
                    amount=value;
                }
                SetParagraphSpacing(target,before,amount);
                initial=field.Text;
                return true;
            };
            editor.SelectionChanged+=delegate{if(target==null)refresh();};
            editor.TextChanged+=delegate{if(target==null)refresh();};
            field.GotKeyboardFocus+=delegate
            {
                target=FormattingRange();initial=field.Text;field.SelectAll();
            };
            field.PreviewKeyDown+=delegate(object sender,KeyEventArgs e)
            {
                if(target==null||(e.Key!=Key.Enter&&e.Key!=Key.Escape))return;
                e.Handled=true;
                if(e.Key==Key.Escape)field.Text=initial;
                if(commit())FocusEditorAt(target);
                else field.SelectAll();
            };
            field.LostKeyboardFocus+=delegate
            {
                commit();
                target=null;
                refresh();
            };
            refresh();
        }

        IEnumerable<Paragraph> SpacingParagraphs(TextRange range)
        {
            if(range.IsEmpty)
            {
                if(range.Start.Paragraph!=null)yield return range.Start.Paragraph;
                yield break;
            }
            foreach(var paragraph in NoteDocument.Paragraphs(editor.Document.Blocks))
            {
                if(paragraph.ContentStart.CompareTo(range.End)>=0)yield break;
                if(paragraph.ContentEnd.CompareTo(range.Start)>=0)yield return paragraph;
            }
        }

        void SetParagraphSpacing(TextRange range,bool before,double amount)
        {
            if(range==null||!range.Start.IsInSameDocument(editor.Document.ContentStart))return;
            var paragraphs=SpacingParagraphs(range).ToArray();
            if(paragraphs.Length==0)return;
            bool changed=false;
            editor.BeginChange();
            try
            {
                foreach(var paragraph in paragraphs)
                {
                    var margin=paragraph.Margin;
                    if((before?margin.Top:margin.Bottom).Equals(amount))continue;
                    if(before)margin.Top=amount;else margin.Bottom=amount;
                    paragraph.Margin=margin;
                    changed=true;
                }
            }
            finally{editor.EndChange();}
            if(changed){bodyDirty=true;QueueSave();}
        }
    }
}
