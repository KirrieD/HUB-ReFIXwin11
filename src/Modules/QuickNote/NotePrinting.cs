using System;
using System.Linq;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReFix.QuickNote
{
    public sealed class NotePageOptions
    {
        public string Paper { get; set; }
        public bool Landscape { get; set; }
        public string Input { get; set; }
        public double Margin { get; set; }
    }

    internal sealed partial class NoteEditor
    {
        static readonly string[] papers = { "A4", "A3", "A5", "Letter", "Legal" };
        static readonly PageMediaSizeName[] media = { PageMediaSizeName.ISOA4, PageMediaSizeName.ISOA3, PageMediaSizeName.ISOA5, PageMediaSizeName.NorthAmericaLetter, PageMediaSizeName.NorthAmericaLegal };
        static readonly Size[] sizes = { Mm(210,297), Mm(297,420), Mm(148,210), new Size(816,1056), new Size(816,1344) };
        PrintQueue printer;
        PrintTicket printerTicket;

        static Size Mm(double w,double h) { return new Size(w*96/25.4,h*96/25.4); }
        NotePageOptions PageOptions
        {
            get { return library.Index.PageOptions ?? (library.Index.PageOptions = new NotePageOptions { Paper = "A4", Margin = 20 }); }
        }
        int PaperIndex { get { return Math.Max(0,Array.IndexOf(papers,PageOptions.Paper)); } }
        void PageSetup()
        {
            var size = new ComboBox { ItemsSource = papers, SelectedIndex = PaperIndex };
            var orientation = new ComboBox { ItemsSource = new[] { "Вертикальная", "Горизонтальная" }, SelectedIndex = PageOptions.Landscape ? 1 : 0 };
            var source = new ComboBox();
            var margin = Field(PageOptions.Margin.ToString(System.Globalization.CultureInfo.CurrentCulture),8);
            var panel = new StackPanel { Margin = new Thickness(16) };
            var printerName = new TextBlock();
            Action sources = delegate
            {
                var options = new System.Collections.Generic.List<string> { "По умолчанию" };
                try
                {
                    if (printer == null) using (var server = new LocalPrintServer()) printer = server.DefaultPrintQueue;
                    if (printer != null) options.AddRange(printer.GetPrintCapabilities(printerTicket).InputBinCapability.Select(b=>b.ToString()));
                }
                catch (PrintSystemException) {}
                printerName.Text = printer == null ? "Принтер не выбран" : printer.FullName;
                source.ItemsSource = options;
                source.SelectedItem = PageOptions.Input;
                if (source.SelectedIndex < 0) source.SelectedIndex = 0;
            };
            sources();
            panel.Children.Add(printerName);
            panel.Children.Add(Button("Выбрать принтер…", delegate
            {
                var dialog = CreatePrintDialog();
                if (dialog.ShowDialog() != true) return;
                printer = dialog.PrintQueue; printerTicket = dialog.PrintTicket; sources();
            }));
            foreach (var item in new Tuple<string,Control>[] { Tuple.Create<string,Control>("Размер бумаги",size),Tuple.Create<string,Control>("Ориентация",orientation),Tuple.Create<string,Control>("Источник бумаги",source),Tuple.Create<string,Control>("Поля, мм",margin) })
            {
                panel.Children.Add(new TextBlock { Text = item.Item1, Margin = new Thickness(0,8,0,3) });
                panel.Children.Add(item.Item2);
            }
            var window = new Window { Owner = Window.GetWindow(this), Title = "Параметры страницы", Width = 350, SizeToContent = SizeToContent.Height, WindowStartupLocation = WindowStartupLocation.CenterOwner, Content = panel };
            var apply = Button("Сохранить", delegate
            {
                double value;
                if (!Double.TryParse(margin.Text,out value) || value < 0 || value > 45 || Double.IsNaN(value)) { margin.ToolTip = "Введите от 0 до 45 мм"; return; }
                var old = library.Index.PageOptions;
                library.Index.PageOptions = new NotePageOptions { Paper=papers[size.SelectedIndex],Landscape=orientation.SelectedIndex==1,Input=source.SelectedIndex<=0?null:(string)source.SelectedItem,Margin=value };
                try { library.Save(); } catch (Exception ex) { library.Index.PageOptions = old; MessageBox.Show(window,ex.Message); return; }
                window.DialogResult = true;
            });
            apply.IsDefault = true; panel.Children.Add(apply);
            var cancel = Button("Отмена",window.Close); cancel.IsCancel = true; panel.Children.Add(cancel);
            window.ShowDialog();
        }

        PrintDialog CreatePrintDialog()
        {
            var dialog = new PrintDialog { UserPageRangeEnabled = true };
            if (printer != null) dialog.PrintQueue = printer;
            var ticket = printerTicket == null ? dialog.PrintTicket : printerTicket.Clone();
            if (ticket != null)
            {
                ticket.PageMediaSize = new PageMediaSize(media[PaperIndex]);
                ticket.PageOrientation = PageOptions.Landscape ? PageOrientation.Landscape : PageOrientation.Portrait;
                InputBin bin;
                if (Enum.TryParse(PageOptions.Input,out bin)) ticket.InputBin = bin;
                dialog.PrintTicket = ticket;
            }
            return dialog;
        }

        DocumentPaginator Paginate(Size pageSize)
        {
            var document = OutputDocument();
            document.Foreground = Brushes.Black;
            document.Background = Brushes.White;
            foreach (var paragraph in NoteDocument.Paragraphs(document.Blocks))
                foreach (var run in NoteDocument.Runs(paragraph.Inlines))
                {
                    var brush = run.Background as SolidColorBrush;
                    if (brush != null && brush.Opacity == 0 && brush.Color.A != 0) run.Background = new SolidColorBrush(brush.Color);
                }
            double margin = Math.Max(0,Math.Min(45,PageOptions.Margin))*96/25.4;
            document.PageWidth = pageSize.Width;
            document.PageHeight = pageSize.Height;
            document.PagePadding = new Thickness(margin);
            document.ColumnWidth = double.PositiveInfinity;
            var paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
            paginator.PageSize = pageSize;
            return paginator;
        }

        void PrintPreview()
        {
            if (!Save()) return;
            Size size = sizes[PaperIndex];
            if (PageOptions.Landscape) size = new Size(size.Height,size.Width);
            var pages = Paginate(size);
            pages.ComputePageCount();
            int current = 0;
            var image = new Image { Stretch = Stretch.Uniform, Margin = new Thickness(12) };
            var status = new TextBlock { VerticalAlignment = VerticalAlignment.Center };
            Action refresh = delegate
            {
                var page = pages.GetPage(current);
                if (page == DocumentPage.Missing) return;
                var visual = new DrawingVisual();
                using(var drawing = visual.RenderOpen())
                {
                    drawing.DrawRectangle(Brushes.White,null,new Rect(size));
                    drawing.DrawRectangle(new VisualBrush(page.Visual),null,new Rect(size));
                }
                var bitmap = new RenderTargetBitmap((int)Math.Ceiling(size.Width),(int)Math.Ceiling(size.Height),96,96,PixelFormats.Pbgra32);
                bitmap.Render(visual);
                image.Source = bitmap;
                status.Text = (current+1) + " / " + pages.PageCount + " · " + PageOptions.Paper + (PageOptions.Landscape ? " · горизонтальная" : " · вертикальная");
            };
            var root = new DockPanel();
            var nav = new WrapPanel { Margin = new Thickness(8) };
            nav.Children.Add(Button("‹", delegate { if (current > 0) { current--; refresh(); } }));
            nav.Children.Add(Button("›", delegate { if (current+1 < pages.PageCount) { current++; refresh(); } }));
            nav.Children.Add(status);
            Add(root,nav,Dock.Top);
            root.Children.Add(image);
            refresh();
            new Window { Owner=Window.GetWindow(this),Title="Предварительный просмотр · "+note.Name,Width=740,Height=690,Content=root,WindowStartupLocation=WindowStartupLocation.CenterOwner }.ShowDialog();
        }

        void Print()
        {
            if (!Save()) return;
            var dialog = CreatePrintDialog();
            if (dialog.ShowDialog() != true) return;
            var ticket = dialog.PrintTicket;
            var mediaSize = ticket == null ? null : ticket.PageMediaSize;
            var page = mediaSize != null && mediaSize.Width.HasValue && mediaSize.Height.HasValue ? new Size(mediaSize.Width.Value,mediaSize.Height.Value) : sizes[PaperIndex];
            bool landscape = ticket == null ? PageOptions.Landscape : ticket.PageOrientation == PageOrientation.Landscape || ticket.PageOrientation == PageOrientation.ReverseLandscape;
            if (landscape && page.Height > page.Width) page = new Size(page.Height,page.Width);
            DocumentPaginator paginator = Paginate(page);
            if (dialog.PageRangeSelection == PageRangeSelection.UserPages)
            {
                paginator.ComputePageCount();
                int first = Math.Max(0,dialog.PageRange.PageFrom-1);
                int end = Math.Min(paginator.PageCount,dialog.PageRange.PageTo);
                if (first >= end) throw new InvalidOperationException("В выбранном диапазоне нет страниц.");
                paginator = new PageSubset(paginator,first,end-first);
            }
            dialog.PrintDocument(paginator,note.Name);
        }

        sealed class PageSubset : DocumentPaginator
        {
            readonly DocumentPaginator inner;
            readonly int first,count;
            internal PageSubset(DocumentPaginator inner,int first,int count) { this.inner=inner;this.first=first;this.count=count; }
            public override DocumentPage GetPage(int pageNumber) { return pageNumber<0||pageNumber>=count?DocumentPage.Missing:inner.GetPage(first+pageNumber); }
            public override bool IsPageCountValid { get { return true; } }
            public override int PageCount { get { return count; } }
            public override Size PageSize { get { return inner.PageSize; } set { inner.PageSize=value; } }
            public override IDocumentPaginatorSource Source { get { return inner.Source; } }
        }
    }
}
