using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using ReFix.Shared;

namespace ReFix.QuickNote
{
    internal sealed partial class NoteEditor
    {
        internal Action<bool> CreateNote;
        TextBox query, replacement;
        TextBlock matchStatus;
        CheckBox matchCase;
        ComboBox replaceMode;
        DockPanel findPane;
        StackPanel replacePane;
        List<TextRange> matches = new List<TextRange>();
        int activeMatch = -1;
        int zoom = 100;
        bool finding, editingMatches;
        bool showSymbols;
        static readonly Lazy<object[]> installedFonts = new Lazy<object[]>(() => new object[] { "Шрифт" }.Concat(Fonts.SystemFontFamilies.OrderBy(f => f.Source).Cast<object>()).ToArray());

        void ConfigureWorkspace()
        {
            var menu = new Menu();
            ThemeMenu.Apply(menu);
            menu.SetResourceReference(Control.BackgroundProperty, "Hub.Surface");
            menu.SetResourceReference(Control.ForegroundProperty, "Hub.Foreground");
            var file = Section(menu, "Файл");
            Command(file, "Новая заметка", () => CreateNote(false));
            Command(file, "Новая заметка MD", () => CreateNote(true));
            Command(file, "Переименовать заметку…", Rename, "F2");
            Command(file, "Последние изменения…", History);
            Command(file, "Сохранить", () => Save(), "Ctrl+S");
            Command(file, "Сохранить как…", () => ExecuteSafely(Export), "Ctrl+Shift+S");
            file.Items.Add(new Separator());
            Command(file, "Параметры страницы…", () => ExecuteSafely(PageSetup));
            Command(file, "Предварительный просмотр…", () => ExecuteSafely(PrintPreview));
            Command(file, "Печать…", () => ExecuteSafely(Print), "Ctrl+P");
            var edit = Section(menu, "Изменить");
            Command(edit, "Отменить", () => editor.Undo(), "Ctrl+Z");
            Command(edit, "Восстановить", () => editor.Redo(), "Ctrl+Y");
            edit.Items.Add(new Separator());
            Command(edit, "Вырезать", () => ExecuteSafely(() => CopySelection(true)), "Ctrl+X");
            Command(edit, "Копировать", () => ExecuteSafely(() => CopySelection(false)), "Ctrl+C");
            Command(edit, "Вставить", () => ExecuteSafely(editor.Paste), "Ctrl+V");
            Command(edit, "Удалить", () => editor.Selection.Text = "", "Del");
            Command(edit, "Найти / Заменить…", () => OpenFind(false), "Ctrl+F");
            Command(edit, "Перейти к строке…", GoToLine, "Ctrl+G");
            Command(edit, "Выбрать всё", editor.SelectAll, "Ctrl+A");
            Command(edit, "Время и дата", () => InsertText(DateTime.Now.ToString("g", CultureInfo.CurrentCulture)), "F5");
            var view = Section(menu, "Просмотр");
            var magnification = new MenuItem { Header = "Масштаб" };
            view.Items.Add(magnification);
            Command(magnification, "Увеличить", () => SetZoom(zoom + 10));
            Command(magnification, "Уменьшить", () => SetZoom(zoom - 10));
            Command(magnification, "Восстановить по умолчанию", () => SetZoom(100));
            var state = new MenuItem { Header = "Строка состояния", IsCheckable = true, IsChecked = true };
            state.Click += delegate { status.Visibility = state.IsChecked ? Visibility.Visible : Visibility.Collapsed; };
            view.Items.Add(state);
            if (note.Markdown) Command(view, "Предпросмотр Markdown…", () => ExecuteSafely(PreviewMarkdown));
            DockPanel.SetDock(menu, Dock.Top);
            root.Children.Insert(0, menu);

            editor.Loaded += delegate
            {
                var host = Window.GetWindow(editor);
                if (host == null) return;
                var context = ThemeMenu.Create(host.Resources);
                Command(context, "Вырезать", () => ExecuteSafely(() => CopySelection(true)), "Ctrl+X");
                Command(context, "Копировать", () => ExecuteSafely(() => CopySelection(false)), "Ctrl+C");
                Command(context, "Вставить", () => ExecuteSafely(editor.Paste), "Ctrl+V");
                context.Items.Add(new Separator());
                Command(context, "Выбрать всё", editor.SelectAll, "Ctrl+A");
                editor.ContextMenu = context;
            };
            AddFindPane();
            if (note.Markdown)
            {
                tools.Children.Clear();
                tools.Children.Add(Button("Предпросмотр MD", () => ExecuteSafely(PreviewMarkdown)));
                tools.Children.Add(Button("Изображение…", () => ExecuteSafely(InsertMarkdownImage)));
                editor.FontFamily = new FontFamily("Consolas");
            }
            else AddFormatTools();

            editor.SelectionChanged += delegate { UpdateStatus(); };
            editor.TextChanged += delegate { if (finding && !editingMatches) RefreshMatches(false); };
            PreviewKeyDown += WorkspaceKeys;
        }

        void AddFormatTools()
        {
            var fonts = new ComboBox { Width = 145, MaxDropDownHeight = 330, ToolTip = "Шрифт", IsTextSearchEnabled = true, Margin = new Thickness(0,2,6,2) };
            fonts.ItemsSource = installedFonts.Value;
            fonts.SelectedIndex = 0;
            fonts.SelectionChanged += delegate
            {
                var font = fonts.SelectedItem as FontFamily;
                if (font != null) editor.Selection.ApplyPropertyValue(TextElement.FontFamilyProperty, font);
                editor.Focus();
            };
            var sizes = new ComboBox { Width = 57, ToolTip = "Размер шрифта", Margin = new Thickness(0,2,6,2), ItemsSource = new object[] { "Кегль",8,9,10,11,12,14,16,18,20,24,28,32,36,48,72 }, SelectedIndex = 0 };
            sizes.SelectionChanged += delegate { if (sizes.SelectedItem is int) editor.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, (int)sizes.SelectedItem * 4.0 / 3); editor.Focus(); };
            tools.Children.Insert(0, fonts);
            tools.Children.Insert(1, sizes);
            var alignment = new ComboBox { Width = 140, ToolTip = "Выравнивание абзацев", Margin = new Thickness(0,2,6,2) };
            foreach (string label in new[] { "Выравнивание", "Слева", "По центру", "Справа", "По ширине" }) alignment.Items.Add(label);
            alignment.SelectedIndex = 0;
            alignment.SelectionChanged += delegate
            {
                if (alignment.SelectedIndex <= 0) return;
                var value = new[] { TextAlignment.Left, TextAlignment.Center, TextAlignment.Right, TextAlignment.Justify }[alignment.SelectedIndex - 1];
                editor.Selection.ApplyPropertyValue(Paragraph.TextAlignmentProperty, value);
                alignment.SelectedIndex = 0;
                editor.Focus();
            };
            tools.Children.Add(alignment);
            Button levels = null;
            levels = Button("Списки ▾", delegate
            {
                var menu = ThemeMenu.Create(Window.GetWindow(this).Resources);
                menu.PlacementTarget = levels;
                foreach (var marker in new[] { TextMarkerStyle.Disc, TextMarkerStyle.Circle, TextMarkerStyle.Square, TextMarkerStyle.Decimal, TextMarkerStyle.LowerLatin, TextMarkerStyle.UpperRoman })
                {
                    string label = new Dictionary<TextMarkerStyle,string>{{TextMarkerStyle.Disc,"● Маркер"},{TextMarkerStyle.Circle,"○ Круг"},{TextMarkerStyle.Square,"■ Квадрат"},{TextMarkerStyle.Decimal,"1. 2. 3."},{TextMarkerStyle.LowerLatin,"a. b. c."},{TextMarkerStyle.UpperRoman,"I. II. III."}}[marker];
                    Command(menu, label, () => SetMarker(marker));
                }
                menu.Items.Add(new Separator());
                Command(menu, "Увеличить уровень", () => EditingCommands.IncreaseIndentation.Execute(null, editor));
                Command(menu, "Уменьшить уровень", () => EditingCommands.DecreaseIndentation.Execute(null, editor));
                menu.IsOpen = true;
            });
            tools.Children.Add(levels);
            tools.Children.Add(Button("☺", delegate { editor.Focus(); EmojiPicker.Open(); }));
            tools.Children.Add(Button("¶", delegate { showSymbols = !showSymbols; highlights.ShowSymbols = showSymbols; highlights.InvalidateVisual(); }));
        }

        void SetMarker(TextMarkerStyle marker)
        {
            editor.Focus();
            var item = editor.CaretPosition.Paragraph == null ? null : editor.CaretPosition.Paragraph.Parent as ListItem;
            if (item == null)
            {
                (marker == TextMarkerStyle.Decimal || marker == TextMarkerStyle.LowerLatin || marker == TextMarkerStyle.UpperRoman ? EditingCommands.ToggleNumbering : EditingCommands.ToggleBullets).Execute(null, editor);
                item = editor.CaretPosition.Paragraph == null ? null : editor.CaretPosition.Paragraph.Parent as ListItem;
            }
            var list = item == null ? null : item.Parent as System.Windows.Documents.List;
            if (list != null) { list.MarkerStyle = marker; bodyDirty = true; QueueSave(); }
        }

        static MenuItem Section(ItemsControl parent, string text)
        {
            var item = new MenuItem { Header = text };
            parent.Items.Add(item);
            return item;
        }
        static void Command(ItemsControl parent, string text, Action action, string shortcut = "")
        {
            var item = Section(parent, text);
            item.InputGestureText = shortcut;
            item.Click += delegate { action(); };
        }
        void ExecuteSafely(Action action)
        {
            try { action(); }
            catch (Exception ex) { MessageBox.Show(ex.Message, "Быстрая заметка", MessageBoxButton.OK, MessageBoxImage.Warning); }
        }
        void Rename() { name.Visibility = Visibility.Visible; name.Focus(); name.SelectAll(); }
        void InsertText(string value) { editor.Selection.Text = value; editor.CaretPosition = editor.Selection.End; editor.Focus(); }
        void SetZoom(int value)
        {
            zoom = Math.Max(50, Math.Min(250, value));
            ((Grid)editor.Parent).LayoutTransform = new ScaleTransform(zoom / 100.0, zoom / 100.0);
            UpdateStatus();
        }
        void WorkspaceKeys(object sender, KeyEventArgs e)
        {
            if (e.Handled) return;
            if (e.Key == Key.F2) { Rename(); e.Handled = true; return; }
            if (e.Key == Key.F5) { InsertText(DateTime.Now.ToString("g")); e.Handled = true; return; }
            if (e.Key == Key.F3 && finding) { SelectMatch(Keyboard.Modifiers == ModifierKeys.Shift ? -1 : 1); e.Handled = true; return; }
            if (Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift) && e.Key == Key.S) { ExecuteSafely(Export); e.Handled = true; return; }
            if (Keyboard.Modifiers != ModifierKeys.Control) return;
            switch (e.Key)
            {
                case Key.F: OpenFind(false); break;
                case Key.H: OpenFind(true); break;
                case Key.G: GoToLine(); break;
                case Key.P: ExecuteSafely(Print); break;
                case Key.C: if (!editor.IsKeyboardFocusWithin) return; ExecuteSafely(() => CopySelection(false)); break;
                case Key.X: if (!editor.IsKeyboardFocusWithin) return; ExecuteSafely(() => CopySelection(true)); break;
                default: return;
            }
            e.Handled = true;
        }

        void AddFindPane()
        {
            query = Field("", 1000);
            query.Width = 180;
            replacement = Field("", 1000);
            replacement.Width = 180;
            matchStatus = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5) };
            matchCase = new CheckBox { Content = "С учётом регистра", VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(5) };
            var top = new WrapPanel();
            top.Children.Add(query);
            top.Children.Add(Button("Найти", () => { RefreshMatches(false); SelectMatch(1); }));
            top.Children.Add(matchStatus);
            top.Children.Add(Button("↑", () => SelectMatch(-1)));
            top.Children.Add(Button("↓", () => SelectMatch(1)));
            top.Children.Add(matchCase);
            top.Children.Add(Button("Заменить ▾", () => replacePane.Visibility = replacePane.IsVisible ? Visibility.Collapsed : Visibility.Visible));
            top.Children.Add(Button("×", CloseFind));

            replaceMode = new ComboBox { Width = 210, Margin = new Thickness(6,0,6,0) };
            foreach (string label in new[] { "Выбранное совпадение", "Все совпадения", "По одному сверху вниз", "По одному снизу вверх" }) replaceMode.Items.Add(label);
            replaceMode.SelectedIndex = 0;
            var bottom = new WrapPanel();
            bottom.Children.Add(replacement);
            bottom.Children.Add(replaceMode);
            bottom.Children.Add(Button("Заменить", Replace));
            replacePane = new StackPanel { Visibility = Visibility.Collapsed };
            replacePane.Children.Add(bottom);
            findPane = new DockPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0,4,0,4) };
            Add(findPane, top, Dock.Top);
            findPane.Children.Add(replacePane);
            DockPanel.SetDock(findPane, Dock.Top);
            root.Children.Insert(root.Children.IndexOf(tools) + 1, findPane);
            query.TextChanged += delegate { RefreshMatches(false); };
            matchCase.Click += delegate { RefreshMatches(false); };
            replaceMode.SelectionChanged += delegate { activeMatch = -1; UpdateMatchStatus(); };
            query.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { SelectMatch(1); e.Handled = true; } };
            findPane.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Escape) { CloseFind(); e.Handled = true; } };
        }

        void OpenFind(bool replace)
        {
            finding = true;
            findPane.Visibility = Visibility.Visible;
            if (replace) replacePane.Visibility = Visibility.Visible;
            RefreshMatches(false);
            query.Focus(); query.SelectAll();
        }
        void CloseFind()
        {
            finding = false;
            findPane.Visibility = Visibility.Collapsed;
            matches.Clear();
            highlights.SearchRanges = matches;
            highlights.InvalidateVisual();
            editor.Focus();
        }
        void RefreshMatches(bool retain)
        {
            if (!finding) return;
            matches = NoteSearch.Find(editor.Document, query.Text, matchCase.IsChecked == true);
            activeMatch = retain ? Math.Min(activeMatch, matches.Count - 1) : -1;
            highlights.SearchRanges = matches;
            highlights.InvalidateVisual();
            UpdateMatchStatus();
        }
        void UpdateMatchStatus() { matchStatus.Text = activeMatch < 0 ? matches.Count + " найдено" : (activeMatch + 1) + " / " + matches.Count; }
        void SelectMatch(int direction)
        {
            if (matches.Count == 0) return;
            activeMatch = activeMatch < 0 ? (direction > 0 ? 0 : matches.Count - 1) : (activeMatch + direction + matches.Count) % matches.Count;
            var range = matches[activeMatch];
            editor.Selection.Select(range.Start, range.End);
            if (range.Start.Paragraph != null) range.Start.Paragraph.BringIntoView();
            UpdateMatchStatus();
        }
        void Replace()
        {
            if (matches.Count == 0) return;
            int mode = replaceMode.SelectedIndex;
            if (activeMatch < 0 && mode != 1)
            {
                if (mode == 0) { SelectMatch(1); return; }
                activeMatch = mode == 3 ? matches.Count - 1 : 0;
            }
            var targets = mode == 1 ? Enumerable.Reverse(matches).ToArray() : new[] { matches[activeMatch] };
            var anchor = mode == 2 ? targets[0].End.GetPositionAtOffset(0, LogicalDirection.Forward) : targets[0].Start.GetPositionAtOffset(0, LogicalDirection.Backward);
            editingMatches = true;
            editor.LockCurrentUndoUnit();
            editor.BeginChange();
            try { foreach (var target in targets) target.Text = replacement.Text; }
            finally { editor.EndChange(); editor.LockCurrentUndoUnit(); editingMatches = false; }
            RefreshMatches(false);
            if (mode == 2 || mode == 3)
            {
                var next = mode == 2 ? matches.FindIndex(r => r.Start.CompareTo(anchor) >= 0) : matches.FindLastIndex(r => r.Start.CompareTo(anchor) < 0);
                if (next >= 0) { activeMatch = next; var range = matches[next]; editor.Selection.Select(range.Start, range.End); if(range.Start.Paragraph != null) range.Start.Paragraph.BringIntoView(); }
                UpdateMatchStatus();
            }
        }

        void GoToLine()
        {
            var field = Field("", 12);
            var panel = new StackPanel { Margin = new Thickness(14) };
            panel.Children.Add(new TextBlock { Text = "Номер строки" }); panel.Children.Add(field);
            var dialog = new Window { Owner = Window.GetWindow(this), Title = "Перейти", Content = panel, Width = 280, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner };
            var go = Button("Перейти", delegate
            {
                int number; var lines = NoteSearch.Lines(editor.Document).ToArray();
                if (!Int32.TryParse(field.Text, out number) || number < 1 || number > lines.Length) { field.ToolTip = "От 1 до " + lines.Length; field.SelectAll(); return; }
                editor.CaretPosition = lines[number - 1]; if(editor.CaretPosition.Paragraph!=null) editor.CaretPosition.Paragraph.BringIntoView(); dialog.DialogResult = true;
            });
            go.IsDefault = true;
            panel.Children.Add(go);
            var cancel = Button("Отмена", dialog.Close); cancel.IsCancel = true; panel.Children.Add(cancel);
            dialog.Loaded += delegate { field.Focus(); };
            dialog.ShowDialog(); editor.Focus();
        }

        void CopySelection(bool cut)
        {
            if (editor.Selection.IsEmpty) return;
            var selection = editor.Selection;
            var image = selection.Start.GetAdjacentElement(LogicalDirection.Forward) as InlineUIContainer;
            var bitmap = image == null ? null : (image.Child as Image);
            if (bitmap != null && bitmap.Source is System.Windows.Media.Imaging.BitmapSource && selection.End.CompareTo(image.ElementEnd) <= 0)
                Clipboard.SetImage((System.Windows.Media.Imaging.BitmapSource)bitmap.Source);
            else
            {
                var data = new DataObject();
                data.SetText(selection.Text);
                if (!note.Markdown) SetClipboardFragment(data);
                Clipboard.SetDataObject(data, true);
            }
            if (cut) selection.Text = "";
        }

        string StatusDetails()
        {
            var caret = editor.CaretPosition;
            var lines = NoteSearch.Lines(editor.Document).TakeWhile(p => p.CompareTo(caret) <= 0).ToArray();
            int column = lines.Length == 0 ? 1 : new TextRange(lines[lines.Length - 1], caret).Text.Length + 1;
            return "Строка " + Math.Max(1, lines.Length) + ", столбец " + column + "  ·  " + PlainText().Length + " символов  ·  " + zoom + "%  ·  " + (note.Markdown ? "Markdown" : "Форматированный");
        }

        string PlainText() { return new TextRange(editor.Document.ContentStart, editor.Document.ContentEnd).Text.TrimEnd('\r','\n'); }
    }

    internal static class EmojiPicker
    {
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern void keybd_event(byte key, byte scan, uint flags, UIntPtr extra);
        internal static void Open()
        {
            keybd_event(0x5B,0,0,UIntPtr.Zero); keybd_event(0xBE,0,0,UIntPtr.Zero);
            keybd_event(0xBE,0,2,UIntPtr.Zero); keybd_event(0x5B,0,2,UIntPtr.Zero);
        }
    }
}
