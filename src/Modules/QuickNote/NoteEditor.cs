using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReFix.Shared;
using ReFix.Modules;

namespace ReFix.QuickNote
{
    internal sealed partial class NoteEditor : UserControl, IDisposable
    {
        readonly Library library;
        readonly NoteRecord note;
        readonly ControlsStore controls;
        readonly Action<NoteRecord> hidden, updated;
        readonly RichTextBox editor = new RichTextBox();
        readonly TextBox name, topHint, bottomHint;
        readonly TextBlock status = new TextBlock();
        readonly DispatcherTimer autosave;
        readonly RoundedHighlights highlights;
        readonly DockPanel root;
        readonly WrapPanel tools;
        bool bodyDirty, metadataDirty, disposed, saveFailed;

        internal NoteEditor(Library library, NoteRecord note, ControlsStore controls,
            Action<NoteRecord> hidden, Action<NoteRecord> updated)
        {
            this.library = library;
            this.note = note;
            this.controls = controls;
            this.hidden = hidden;
            this.updated = updated;
            root = new DockPanel { Margin = new Thickness(10) };

            name = Field(note.Name, 180);
            name.ToolTip = "Название заметки";
            name.Visibility = Visibility.Collapsed;
            name.LostKeyboardFocus += delegate { name.Visibility = Visibility.Collapsed; Save(); };
            name.PreviewKeyDown += delegate(object sender, KeyEventArgs e) { if (e.Key == Key.Enter) { e.Handled=true; editor.Focus(); } };
            Add(root, name, Dock.Top);

            tools = new WrapPanel { Margin = new Thickness(0, 5, 0, 5) };
            var styles = new ComboBox { MinWidth = 130, Margin = new Thickness(0, 2, 6, 2), ToolTip = "Стиль абзаца" };
            styles.Items.Add(new ComboBoxItem { Content = "Стиль абзаца", IsEnabled = false });
            styles.Items.Add("Обычный текст");
            for (int i = 1; i <= 3; i++) styles.Items.Add("Заголовок " + i);
            for (int i = 1; i <= 3; i++) styles.Items.Add("Подзаголовок " + i);
            styles.SelectedIndex = 0;
            styles.SelectionChanged += delegate { if (styles.SelectedIndex > 0) { ApplyStyle(styles.SelectedIndex - 1); styles.SelectedIndex = 0; } };
            styles.IsEditable = false;
            tools.Children.Add(styles);
            tools.Children.Add(FormatButton("B", "Жирный", () => EditingCommands.ToggleBold.Execute(null, editor), FontWeights.Bold, FontStyles.Normal, null));
            tools.Children.Add(FormatButton("I", "Курсив", () => EditingCommands.ToggleItalic.Execute(null, editor), FontWeights.Normal, FontStyles.Italic, null));
            tools.Children.Add(FormatButton("S", "Зачёркивание", () => ToggleDecoration(TextDecorationLocation.Strikethrough, TextDecorations.Strikethrough[0]), FontWeights.Normal, FontStyles.Normal, TextDecorations.Strikethrough));
            tools.Children.Add(FormatButton("P", "Подчёркивание", () => ToggleDecoration(TextDecorationLocation.Underline, TextDecorations.Underline[0]), FontWeights.Normal, FontStyles.Normal, TextDecorations.Underline));
            tools.Children.Add(FormatButton("• ≡", "Маркированный список", () => EditingCommands.ToggleBullets.Execute(null, editor), FontWeights.Normal, FontStyles.Normal, null));
            tools.Children.Add(FormatButton("1. ≡", "Нумерованный список", () => EditingCommands.ToggleNumbering.Execute(null, editor), FontWeights.Normal, FontStyles.Normal, null));
            Button textColor = null, background = null;
            textColor = Button("Цвет текста", () => Palette(textColor, TextElement.ForegroundProperty));
            background = Button("Цвет фона", () => Palette(background, TextElement.BackgroundProperty));
            tools.Children.Add(textColor);
            tools.Children.Add(background);
            tools.Children.Add(Button("Вставить изображение", SelectImages));
            Add(root, tools, Dock.Top);

            topHint = Hint(root, "Верхняя подсказка", note.TopHint, Dock.Top);
            status.Margin = new Thickness(0, 8, 0, 0);
            status.SetResourceReference(TextBlock.ForegroundProperty, "Hub.Muted");
            Add(root, status, Dock.Bottom);
            bottomHint = Hint(root, "Нижняя подсказка", note.BottomHint, Dock.Bottom);

            editor.AcceptsTab = true;
            editor.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            editor.Padding = new Thickness(12);
            editor.Background = Brushes.Transparent;
            editor.SetResourceReference(Control.ForegroundProperty, "Hub.Foreground");
            editor.SetResourceReference(Control.BorderBrushProperty, "Hub.Border");
            editor.Document.FontSize = 15;
            library.LoadBody(note, editor.Document);
            var page = new Grid { ClipToBounds = true };
            page.SetResourceReference(Panel.BackgroundProperty, "Hub.Surface");
            page.Children.Add(editor);
            highlights = new RoundedHighlights(editor);
            Panel.SetZIndex(highlights, -1);
            page.Children.Add(highlights);
            root.Children.Add(page);
            Content = root;

            autosave = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(650) };
            autosave.Tick += delegate { autosave.Stop(); Save(); };
            editor.TextChanged += delegate { bodyDirty = true; QueueSave(); };
            foreach (var field in new[] { name, topHint, bottomHint }) field.TextChanged += delegate { metadataDirty = true; QueueSave(); };
            DataObject.AddPastingHandler(editor, Paste);
            editor.PreviewDragOver += delegate(object sender, DragEventArgs e) { if (e.Data.GetDataPresent(DataFormats.FileDrop)) { e.Effects = DragDropEffects.Copy; e.Handled = true; } };
            editor.PreviewDrop += delegate(object sender, DragEventArgs e)
            {
                var paths = e.Data.GetData(DataFormats.FileDrop) as string[];
                if (paths == null) return;
                var point = editor.GetPositionFromPoint(e.GetPosition(editor), true);
                if (point != null) editor.CaretPosition = point;
                InsertFiles(paths);
                e.Handled = true;
            };
            controls.Changed += ControlsChanged;
            PreviewKeyDown += HandleKey;
            ConfigureWorkspace();
            UpdateStatus();
        }

        static void Add(DockPanel root, UIElement element, Dock dock) { DockPanel.SetDock(element, dock); root.Children.Add(element); }
        internal static Button Button(string text, Action action)
        {
            var button = new Button { Content = text, Margin = new Thickness(0, 2, 6, 2), Padding = new Thickness(9, 4, 9, 4) };
            button.Click += delegate { action(); };
            return button;
        }

        Button FormatButton(string label, string description, Action apply, FontWeight weight, FontStyle style, TextDecorationCollection decorations)
        {
            var button = Button("", delegate { editor.Focus(); apply(); });
            button.Content = new TextBlock { Text = label, FontSize = 15, FontWeight = weight, FontStyle = style, TextDecorations = decorations };
            button.ToolTip = description;
            button.Focusable = false;
            System.Windows.Automation.AutomationProperties.SetName(button, description);
            return button;
        }

        void ToggleDecoration(TextDecorationLocation location, TextDecoration decoration)
        {
            var selection = editor.Selection;
            var old = selection.GetPropertyValue(Inline.TextDecorationsProperty) as TextDecorationCollection;
            bool remove = old != null && old.Any(d => d.Location == location);
            if (selection.IsEmpty)
            {
                selection.ApplyPropertyValue(Inline.TextDecorationsProperty, ChangeDecoration(old, location, decoration, remove));
                return;
            }
            var start = selection.Start;
            var end = selection.End;
            var parts = NoteDocument.Paragraphs(editor.Document.Blocks).SelectMany(p => NoteDocument.Runs(p.Inlines))
                .Where(r => r.ContentStart.CompareTo(end) < 0 && r.ContentEnd.CompareTo(start) > 0)
                .Select(r => new
                {
                    Start = r.ContentStart.CompareTo(start) < 0 ? start : r.ContentStart,
                    End = r.ContentEnd.CompareTo(end) > 0 ? end : r.ContentEnd,
                    Value = ChangeDecoration(r.TextDecorations, location, decoration, remove)
                }).ToArray();
            editor.BeginChange();
            try { foreach (var part in parts) new TextRange(part.Start, part.End).ApplyPropertyValue(Inline.TextDecorationsProperty, part.Value); }
            finally { editor.EndChange(); }
        }

        static TextDecorationCollection ChangeDecoration(TextDecorationCollection old, TextDecorationLocation location, TextDecoration decoration, bool remove)
        {
            var result = new TextDecorationCollection();
            if (old != null) foreach (var item in old) if (item.Location != location) result.Add(item);
            if (!remove) result.Add(decoration);
            return result;
        }

        static TextBox Field(string value, int limit)
        {
            var field = new TextBox { Text = value ?? "", MaxLength = limit, Padding = new Thickness(5) };
            field.SetResourceReference(Control.BackgroundProperty, "Hub.Surface");
            field.SetResourceReference(Control.ForegroundProperty, "Hub.Foreground");
            return field;
        }

        static TextBox Hint(DockPanel root, string label, string value, Dock dock)
        {
            var box = Field(value, 2000);
            box.AcceptsReturn = true;
            box.TextWrapping = TextWrapping.Wrap;
            box.MaxHeight = 90;
            box.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            var group = new Expander { Header = label, Content = box, IsExpanded = !String.IsNullOrEmpty(value), Margin = new Thickness(0, 5, 0, 5) };
            Add(root, group, dock);
            return box;
        }

        void ApplyStyle(int style)
        {
            var selection = editor.Selection;
            var first = selection.Start.Paragraph;
            double[] sizes = { 15, 29, 24, 21, 19, 17, 16 };
            var last = (selection.IsEmpty ? selection.End : selection.End.GetInsertionPosition(LogicalDirection.Backward)).Paragraph;
            foreach (var paragraph in NoteDocument.Paragraphs(editor.Document.Blocks).SkipWhile(p => p != first))
            {
                new TextRange(paragraph.ContentStart, paragraph.ContentEnd).ApplyPropertyValue(TextElement.FontSizeProperty, sizes[style]);
                new TextRange(paragraph.ContentStart, paragraph.ContentEnd).ApplyPropertyValue(TextElement.FontWeightProperty, style == 0 ? FontWeights.Normal : FontWeights.SemiBold);
                paragraph.FontSize = sizes[style];
                paragraph.FontWeight = style == 0 ? FontWeights.Normal : FontWeights.SemiBold;
                if (paragraph == last) break;
            }
            editor.Focus();
            bodyDirty = true;
            QueueSave();
        }

        void Palette(Button button, DependencyProperty property)
        {
            var range = new TextRange(editor.Selection.Start, editor.Selection.End);
            var menu = new ContextMenu { PlacementTarget = button, Resources = Window.GetWindow(this).Resources };
            menu.Items.Add(new MenuItem { Header = "Стандартные цвета", IsEnabled = false });
            AddSwatches(menu, new[] { "#000000", "#FFFFFF", "#808080", "#C00000", "#FF0000", "#FFC000", "#FFFF00", "#92D050", "#00B050", "#00B0F0", "#0070C0", "#002060", "#7030A0", "#FF66AA" }, color => ApplyColor(range, property, color));
            if (library.Index.Colors.Count > 0)
            {
                menu.Items.Add(new Separator());
                menu.Items.Add(new MenuItem { Header = "Последние пользовательские цвета", IsEnabled = false });
                AddSwatches(menu, library.Index.Colors, color => ApplyColor(range, property, color));
            }
            var clear = new MenuItem { Header = "Сбросить цвет" };
            clear.Click += delegate { range.ApplyPropertyValue(property, property == TextElement.ForegroundProperty ? editor.Foreground : Brushes.Transparent); bodyDirty = true; QueueSave(); };
            menu.Items.Add(clear);
            var custom = new MenuItem { Header = "Свой цвет · RGB / HEX…" };
            custom.Click += delegate
            {
                var input = Field("#", 32);
                var hint = new TextBlock { Text = "Введите #RRGGBB или три числа RGB через запятую.", Margin = new Thickness(0, 0, 0, 8) };
                var panel = new StackPanel { Margin = new Thickness(16) };
                panel.Children.Add(hint);
                panel.Children.Add(input);
                string chosen = null;
                var dialog = new Window { Owner = Window.GetWindow(this), Title = "Свой цвет", Width = 380, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, ResizeMode = ResizeMode.NoResize, Content = panel };
                var apply = Button("Применить", delegate { if (TryColor(input.Text, out chosen)) dialog.DialogResult = true; else hint.Text = "Введите корректный HEX или RGB от 0 до 255."; });
                apply.IsDefault = true;
                panel.Children.Add(apply);
                dialog.Loaded += delegate { input.Focus(); input.SelectAll(); };
                if (dialog.ShowDialog() != true) return;
                library.Index.Colors.Remove(chosen);
                library.Index.Colors.Insert(0, chosen);
                if (library.Index.Colors.Count > 20) library.Index.Colors.RemoveAt(20);
                metadataDirty = true;
                ApplyColor(range, property, chosen);
            };
            menu.Items.Add(custom);
            menu.IsOpen = true;
        }

        static bool TryColor(string input, out string result)
        {
            result = null;
            input = input.Trim().TrimStart('#');
            int rgb;
            if (input.Length == 6 && Int32.TryParse(input, System.Globalization.NumberStyles.HexNumber, null, out rgb))
                result = "#" + rgb.ToString("X6");
            else
            {
                var parts = input.Split(',');
                byte r, g, b;
                if (parts.Length == 3 && Byte.TryParse(parts[0], out r) && Byte.TryParse(parts[1], out g) && Byte.TryParse(parts[2], out b))
                    result = String.Format("#{0:X2}{1:X2}{2:X2}", r, g, b);
            }
            return result != null;
        }

        static void AddSwatches(ContextMenu menu, System.Collections.Generic.IEnumerable<string> colors, Action<string> apply)
        {
            var palette = new WrapPanel { Width = 198 };
            foreach (string color in colors)
            {
                string valid;
                if (!TryColor(color, out valid)) continue;
                var swatch = new Button { Width = 24, Height = 24, Margin = new Thickness(2), ToolTip = valid, Background = BrushFor(valid) };
                swatch.Click += delegate { menu.IsOpen = false; apply(valid); };
                palette.Children.Add(swatch);
            }
            menu.Items.Add(palette);
        }

        static Brush BrushFor(string hex) { return new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)); }

        void ApplyColor(TextRange range, DependencyProperty property, string hex)
        {
            editor.Focus();
            var brush = property == TextElement.BackgroundProperty ? NoteDocument.Highlight((Color)ColorConverter.ConvertFromString(hex)) : BrushFor(hex);
            if (range.IsEmpty) editor.Selection.ApplyPropertyValue(property, brush);
            else range.ApplyPropertyValue(property, brush);
            bodyDirty = true;
            QueueSave();
        }

        void SelectImages()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog { Multiselect = true, Filter = "Изображения|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.ico;*.img|Все файлы|*.*" };
            if (dialog.ShowDialog(Window.GetWindow(this)) == true) InsertFiles(dialog.FileNames);
        }

        void Paste(object sender, DataObjectPastingEventArgs e)
        {
            if (PasteNoteFragment(e)) return;
            var files = e.DataObject.GetData(DataFormats.FileDrop) as string[];
            if (files != null) { e.CancelCommand(); InsertFiles(files); return; }
            if (e.DataObject.GetData(DataFormats.Bitmap) is BitmapSource)
            {
                e.CancelCommand();
                var source = (BitmapSource)e.DataObject.GetData(DataFormats.Bitmap);
                if (note.Markdown) PasteMarkdownBitmap(source);
                else InsertImage(new Image { Source = source, Width = Math.Min(520, source.Width), Stretch = Stretch.Uniform });
                return;
            }
            if (e.DataObject.GetDataPresent(DataFormats.UnicodeText)) e.FormatToApply = DataFormats.UnicodeText;
            else e.CancelCommand();
        }

        void InsertFiles(string[] paths)
        {
            foreach (string path in paths)
            {
                try
                {
                    if (note.Markdown) AddMarkdownImage(path);
                    else InsertImage(NoteDocument.DecodeImage(File.ReadAllBytes(path)));
                }
                catch (Exception ex) { status.Text = "Не удалось вставить изображение: " + ex.Message; }
            }
        }

        void InsertImage(Image image)
        {
            if (!editor.Selection.IsEmpty) editor.Selection.Text = "";
            TextPointer at = editor.CaretPosition.GetInsertionPosition(LogicalDirection.Forward);
            var inline = new InlineUIContainer(image, at);
            editor.CaretPosition = inline.ElementEnd.GetInsertionPosition(LogicalDirection.Forward);
            editor.Focus();
            bodyDirty = true;
            QueueSave();
        }

        void HandleKey(object sender, KeyEventArgs e)
        {
            var profile = controls.Current;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (finding && key == Key.Escape && Keyboard.Modifiers == ModifierKeys.None) { CloseFind(); e.Handled = true; return; }
            if (KeyChord.Parse(profile.Get("quick-note.hide")).Matches(key, Keyboard.Modifiers)) { e.Handled = true; MinimizeNote(); }
            else if (KeyChord.Parse(profile.Get("quick-note.save")).Matches(key, Keyboard.Modifiers)) { e.Handled = true; Save(); }
        }

        void QueueSave() { saveFailed = false; UpdateStatus(); autosave.Stop(); autosave.Start(); }
        void UpdateStatus()
        {
            if (disposed || saveFailed) return;
            var profile = controls.Current;
            status.Text = StatusDetails() + "  ·  " + (bodyDirty || metadataDirty ? "Изменено" : "Сохранено");
            status.ToolTip = profile.Get("quick-note.save") + " — сохранить · " + profile.Get("quick-note.hide") + " — свернуть";
        }
        void ControlsChanged(object sender, EventArgs e) { if (!Dispatcher.HasShutdownStarted) Dispatcher.BeginInvoke(new Action(UpdateStatus)); }

        internal bool Save()
        {
            autosave.Stop();
            try
            {
                if (bodyDirty) { library.SaveBody(note, editor.Document); bodyDirty = false; }
                if (metadataDirty)
                {
                    note.Name = String.IsNullOrWhiteSpace(name.Text) ? "Без названия" : name.Text.Trim();
                    note.TopHint = topHint.Text;
                    note.BottomHint = bottomHint.Text;
                    library.Save();
                    metadataDirty = false;
                    if (updated != null) updated(note);
                }
                saveFailed = false;
                UpdateStatus();
                return true;
            }
            catch (Exception ex)
            {
                saveFailed = true;
                status.Text = "Не удалось сохранить: " + ex.Message;
                return false;
            }
        }

        internal void MinimizeNote()
        {
            if (!Save()) return;
            if (hidden != null) hidden(note);
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            autosave.Stop();
            controls.Changed -= ControlsChanged;
        }
    }
}
