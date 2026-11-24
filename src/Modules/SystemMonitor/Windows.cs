using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using ReFix.Shared;
using ReFix.Modules;

namespace ReFix.SystemMonitor
{
    internal static class MonitorView
    {
        internal static TextBlock Text(string text,double size=12,string resource="Hub.Foreground") { var t=new TextBlock { Text=text,FontSize=size,VerticalAlignment=VerticalAlignment.Center,TextTrimming=TextTrimming.CharacterEllipsis };t.SetResourceReference(TextBlock.ForegroundProperty,resource);return t; }
        internal static Button Button(string label,Action action) {var b=new Button {Content=label,Padding=new Thickness(9,5,9,5),Margin=new Thickness(3),MinHeight=26};b.Click+=delegate { action(); };return b;}
        internal static string Number(double? n,string suffix) { return n.HasValue&&!Double.IsNaN(n.Value)&&!Double.IsInfinity(n.Value)?n.Value.ToString("0.#",CultureInfo.CurrentCulture)+suffix:"—"; }
        internal static string Bytes(double? n) { if(!n.HasValue)return "—"; double v=n.Value; string[] units={"Б","КБ","МБ","ГБ","ТБ"};int i=0;while(v>=1024&&i<4){v/=1024;i++;}return v.ToString("0.#",CultureInfo.CurrentCulture)+" "+units[i]; }
        internal static string Value(Metric m) {return m==null?"Нет данных":Number(m.Percent," %");}
        internal static string Detail(Metric m) {if(m==null)return "Нет данных";var a=new List<string>();if(m.Temperature.HasValue)a.Add(Number(m.Temperature," °C"));else if(Kind(m)=="cpu"||Kind(m)=="gpu")a.Add("t° —");if(m.FrequencyGHz.HasValue)a.Add(Number(m.FrequencyGHz," ГГц"));if(m.UsedBytes.HasValue)a.Add(Bytes(m.UsedBytes)+" / "+Bytes(m.TotalBytes));if(m.ReadBytesPerSecond.HasValue||m.WriteBytesPerSecond.HasValue)a.Add("↓ "+Bytes(m.ReadBytesPerSecond)+"/с  ↑ "+Bytes(m.WriteBytesPerSecond)+"/с");return a.Count==0?(m.Detail??"Нет дополнительных данных"):String.Join(" · ",a);}
        internal static string Kind(Metric m){return Convert.ToString(m.Kind).ToLowerInvariant();}
        internal static List<double?> History(Dictionary<string,List<double?>> h,string id) {List<double?> v;return h!=null&&h.TryGetValue(id,out v)?v:null;}
        internal static Brush Color(FrameworkElement owner,MonitorConfig c,string kind) {string color;if(c!=null&&c.Colors!=null&&c.Colors.TryGetValue(kind,out color)){try{return ThemeApplier.ColorBrush(color);}catch(FormatException){}}return owner.TryFindResource("Hub.Accent") as Brush??Brushes.DeepSkyBlue;}
    }
    internal abstract class MonitorWindow : Window
    {
        readonly ThemedSurface surface;
        protected readonly DockPanel Body=new DockPanel {Margin=new Thickness(12)};
        protected MonitorConfig config;
        protected MonitorWindow(ThemeProfile theme,double width,double height) {Width=width;Height=height;WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;ThemeControls.Install(this);surface=new ThemedSurface(Body);Content=surface;ApplyTheme(theme);}
        public virtual void ApplyTheme(ThemeProfile theme){surface.Apply(this,theme);}
        public virtual void ApplyConfig(MonitorConfig value){config=value;Topmost=value.Topmost;Opacity=value.Opacity;}
        protected void Header(string title,Action settings,Action moved,bool close,Action open=null) {
            var bar=new DockPanel {Margin=new Thickness(0,0,0,8)};
            var grip=MonitorView.Text("⠿",18,"Hub.Muted");grip.Width=22;grip.Cursor=Cursors.SizeAll;grip.ToolTip="Перетащить окно";
            grip.MouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){if(e.LeftButton==MouseButtonState.Pressed){try{DragMove();}catch(InvalidOperationException){}if(moved!=null)moved();e.Handled=true;}};
            bar.Children.Add(grip);
            if(close){var x=MonitorView.Button("×",delegate{Close();});DockPanel.SetDock(x,Dock.Right);bar.Children.Add(x);}
            if(settings!=null){var menuButton=MonitorView.Button("⋯",delegate{settings();});menuButton.ToolTip="Настройки мониторинга";DockPanel.SetDock(menuButton,Dock.Right);bar.Children.Add(menuButton);}
            var heading=MonitorView.Text(title,14);if(open!=null){heading.Cursor=Cursors.Hand;heading.ToolTip="Открыть мониторинг системы";heading.MouseLeftButtonUp+=delegate {open();};}bar.Children.Add(heading);DockPanel.SetDock(bar,Dock.Top);Body.Children.Add(bar);
        }
    }
    internal sealed class CompactWindow : MonitorWindow
    {
        sealed class Row { internal Grid Panel; internal TextBlock Value, Detail; internal Sparkline Chart; }
        readonly Dictionary<string, Row> rows = new Dictionary<string, Row>();

        public CompactWindow(ThemeProfile theme, Action open, Action settings, Action moved) : base(theme, 346, 226)
        {
            Title = "Мониторинг";
            ShowInTaskbar = false;
            Header("Мониторинг", settings, moved, false, open);
            var stack = new StackPanel();
            Body.Children.Add(stack);
            string[] kinds = { "cpu", "gpu", "memory", "disk" };
            string[] labels = { "ЦП", "GPU", "ОЗУ", "Диск" };
            for (int i = 0; i < kinds.Length; i++)
            {
                var panel = new Grid { Height = 36, Background = Brushes.Transparent, Cursor = Cursors.Hand };
                panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(37) });
                panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(55) });
                panel.ColumnDefinitions.Add(new ColumnDefinition());
                panel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(77) });
                var label = MonitorView.Text(labels[i], 12);
                panel.Children.Add(label);
                var value = MonitorView.Text("—", 13);
                value.FontWeight = FontWeights.SemiBold;
                Grid.SetColumn(value, 1);
                panel.Children.Add(value);
                var chart = new Sparkline { Margin = new Thickness(3, 6, 10, 6), TrimLeadingMissing = true, FillArea = true, FitRange = true };
                Grid.SetColumn(chart, 2);
                panel.Children.Add(chart);
                var detail = MonitorView.Text("—", 11, "Hub.Muted");
                detail.HorizontalAlignment = HorizontalAlignment.Right;
                Grid.SetColumn(detail, 3);
                panel.Children.Add(detail);
                panel.MouseLeftButtonUp += delegate { open(); };
                stack.Children.Add(panel);
                rows[kinds[i]] = new Row { Panel = panel, Value = value, Detail = detail, Chart = chart };
            }
        }

        public override void ApplyConfig(MonitorConfig value)
        {
            base.ApplyConfig(value);
            Body.LayoutTransform = new ScaleTransform(value.Scale, value.Scale);
            int count = 0;
            foreach (var pair in rows)
            {
                bool show = value.VisibleKinds == null || value.VisibleKinds.Contains(pair.Key);
                pair.Value.Panel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
                if (show) count++;
                pair.Value.Chart.LineBrush = MonitorView.Color(this, value, pair.Key);
            }
            Width = 28 + 318 * value.Scale;
            Height = 28 + (62 + 36 * count) * value.Scale;
        }

        public void Update(Snapshot snapshot, Dictionary<string, List<double?>> history)
        {
            foreach (var pair in rows)
            {
                Metric metric = snapshot.Metrics.Where(x => MonitorView.Kind(x) == pair.Key).OrderByDescending(x => x.Percent ?? -1).FirstOrDefault();
                pair.Value.Value.Text = MonitorView.Value(metric);
                pair.Value.Detail.Text = Summary(metric);
                pair.Value.Panel.ToolTip = metric == null ? "Нет данных" : metric.Name + "\n" + MonitorView.Detail(metric) + "\n" + metric.Detail + "\nМасштаб мини-графика адаптируется к диапазону значений.";
                pair.Value.Chart.LineBrush = MonitorView.Color(this, config, pair.Key);
                pair.Value.Chart.Update(metric == null ? null : MonitorView.History(history, metric.Id));
            }
        }

        static string Summary(Metric metric)
        {
            if (metric == null) return "—";
            switch (MonitorView.Kind(metric))
            {
                case "cpu": return metric.Temperature.HasValue ? MonitorView.Number(metric.Temperature, " °C") : MonitorView.Number(metric.FrequencyGHz, " ГГц");
                case "gpu": return metric.Temperature.HasValue ? MonitorView.Number(metric.Temperature, " °C") : MonitorView.Bytes(metric.UsedBytes);
                case "memory": return MonitorView.Bytes(metric.UsedBytes);
                case "disk": return metric.ReadBytesPerSecond.HasValue || metric.WriteBytesPerSecond.HasValue ? MonitorView.Bytes((metric.ReadBytesPerSecond ?? 0) + (metric.WriteBytesPerSecond ?? 0)) + "/с" : "—";
                default: return "—";
            }
        }
    }
    internal sealed class MetricCard : Border
    {
        readonly TextBlock title=MonitorView.Text("",14),value=MonitorView.Text("—",24),detail=MonitorView.Text("",11,"Hub.Muted");
        internal readonly Sparkline Chart=new Sparkline {Height=110,ShowGrid=true,FillArea=true,Margin=new Thickness(0,10,0,5)};
        internal MetricCard(string id,Action<string> detach){Margin=new Thickness(0,0,12,12);Padding=new Thickness(12);BorderThickness=new Thickness(1);CornerRadius=new CornerRadius(8);SetResourceReference(BorderBrushProperty,"Hub.Border");SetResourceReference(BackgroundProperty,"Hub.Surface");var stack=new StackPanel();var head=new DockPanel();if(detach!=null){var button=MonitorView.Button("↗",delegate{detach(id);});button.ToolTip="Отделить график";DockPanel.SetDock(button,Dock.Right);head.Children.Add(button);}head.Children.Add(title);stack.Children.Add(head);stack.Children.Add(value);stack.Children.Add(Chart);detail.TextWrapping=TextWrapping.Wrap;stack.Children.Add(detail);Child=stack;}
        internal void Update(Metric metric,Dictionary<string,List<double?>> history,MonitorConfig cfg){title.Text=metric.Name;value.Text=MonitorView.Value(metric);detail.Text=MonitorView.Detail(metric);ToolTip=metric.Detail;Chart.LineBrush=MonitorView.Color(this,cfg,MonitorView.Kind(metric));Chart.Update(MonitorView.History(history,metric.Id));}
    }
    internal sealed class ChartWindow : MonitorWindow
    {
        readonly string metricId;readonly MetricCard card;readonly TextBlock period=MonitorView.Text("",10,"Hub.Muted");
        public ChartWindow(ThemeProfile theme,string metricId,Action moved):base(theme,350,310){this.metricId=metricId;ShowInTaskbar=false;ResizeMode=ResizeMode.CanResizeWithGrip;MinWidth=280;MinHeight=270;Header("График",null,moved,true);DockPanel.SetDock(period,Dock.Bottom);Body.Children.Add(period);card=new MetricCard(metricId,null);Body.Children.Add(card);SizeChanged+=delegate {if(IsLoaded&&moved!=null)moved();};}
        public override void ApplyConfig(MonitorConfig value){base.ApplyConfig(value);period.Text="История: "+value.HistorySeconds+" с · 0–100 %";}
        public void Update(Snapshot snapshot,Dictionary<string,List<double?>> history){var m=snapshot.Metrics.FirstOrDefault(x=>x.Id==metricId);if(m!=null){Title=m.Name;card.Update(m,history,config);}}
    }
    internal sealed class MainWindow : MonitorWindow
    {
        readonly WrapPanel cards=new WrapPanel();readonly Dictionary<string,MetricCard> cardMap=new Dictionary<string,MetricCard>();
        readonly Action<string> detach;readonly TextBlock status=MonitorView.Text("Ожидание первого измерения",11,"Hub.Muted");
        readonly DataGrid runtime,resources;readonly TextBlock period=MonitorView.Text("",11,"Hub.Muted");
        public MainWindow(ThemeProfile theme,Action settings,Action<string> detach):base(theme,950,650){
            this.detach=detach;Title="Мониторинг системы";ResizeMode=ResizeMode.CanResizeWithGrip;MinWidth=720;MinHeight=480;Header("Мониторинг системы",settings,null,true);
            var footer=new DockPanel();var task=MonitorView.Button("Диспетчер задач",delegate{try{Process.Start(new ProcessStartInfo("taskmgr.exe"){UseShellExecute=true});}catch(Win32Exception){status.Text="Не удалось открыть Диспетчер задач";}});DockPanel.SetDock(task,Dock.Right);footer.Children.Add(task);footer.Children.Add(status);DockPanel.SetDock(footer,Dock.Bottom);Body.Children.Add(footer);
            var tabs=new TabControl();tabs.Resources[typeof(TabItem)]=System.Windows.Markup.XamlReader.Parse(@"<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' TargetType='TabItem'><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='TabItem'><Border x:Name='Frame' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' Background='{DynamicResource Hub.Surface}' BorderBrush='{DynamicResource Hub.Border}' BorderThickness='1' Padding='10,8'><ContentPresenter ContentSource='Header'/></Border><ControlTemplate.Triggers><Trigger Property='IsSelected' Value='True'><Setter TargetName='Frame' Property='Background' Value='{DynamicResource Hub.Background}'/><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource Hub.Accent}'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderThickness' Value='2'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter></Style>");tabs.SetResourceReference(Control.BackgroundProperty,"Hub.Background");tabs.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");tabs.SetResourceReference(Control.BorderBrushProperty,"Hub.Border");
            var performance=new DockPanel();DockPanel.SetDock(period,Dock.Top);performance.Children.Add(period);performance.Children.Add(new ScrollViewer {Content=cards,VerticalScrollBarVisibility=ScrollBarVisibility.Auto});cards.SizeChanged+=delegate {ResizeCards();};AddTab(tabs,"Производительность",performance);
            runtime=Table();Column(runtime,"Приложение / процесс","Name",240);Column(runtime,"PID","Pid",70);Column(runtime,"Фоновый","Background",80);Column(runtime,"Запущен","Started",160);Column(runtime,"Время работы","Runtime",160);
            AddTab(tabs,"Время работы приложений",runtime);
            resources=Table();Column(resources,"Процесс","Name",210);Column(resources,"PID","Pid",65);Column(resources,"ЦП, %","CpuPercent",85);Column(resources,"ОЗУ, МБ","MemoryMB",100);Column(resources,"GPU, %","GpuPercent",85);Column(resources,"Ввод, МБ/с","ReadMB",110);Column(resources,"Вывод, МБ/с","WriteMB",110);
            var resourcePanel=new DockPanel();var note=MonitorView.Text("Сортировка — нажатием заголовка. ЦП — доля всей системы; ввод/вывод включает файлы, сеть и устройства.\n— означает отсутствие измерения. Список включает фоновые процессы.",11,"Hub.Muted");note.TextWrapping=TextWrapping.Wrap;note.Margin=new Thickness(8);DockPanel.SetDock(note,Dock.Top);resourcePanel.Children.Add(note);resourcePanel.Children.Add(resources);AddTab(tabs,"Потребители ресурсов",resourcePanel);Body.Children.Add(tabs);
        }
        void AddTab(TabControl tabs,string title,UIElement content){var item=new TabItem {Header=title,Content=content,Padding=new Thickness(10,8,10,8)};item.SetResourceReference(Control.BackgroundProperty,"Hub.Surface");item.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");tabs.Items.Add(item);}
        static DataGrid Table(){var g=new DataGrid {AutoGenerateColumns=false,IsReadOnly=true,CanUserAddRows=false,CanUserDeleteRows=false,CanUserSortColumns=true,EnableRowVirtualization=true,EnableColumnVirtualization=true,HeadersVisibility=DataGridHeadersVisibility.Column,GridLinesVisibility=DataGridGridLinesVisibility.None,RowHeight=30,BorderThickness=new Thickness(0),SelectionMode=DataGridSelectionMode.Single};g.SetResourceReference(Control.BackgroundProperty,"Hub.Background");g.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");g.SetResourceReference(DataGrid.RowBackgroundProperty,"Hub.Surface");g.SetResourceReference(DataGrid.AlternatingRowBackgroundProperty,"Hub.Background");foreach(var t in new[]{typeof(DataGridColumnHeader),typeof(DataGridCell)}){var s=new Style(t);s.Setters.Add(new Setter(Control.BackgroundProperty,new DynamicResourceExtension("Hub.Surface")));s.Setters.Add(new Setter(Control.ForegroundProperty,new DynamicResourceExtension("Hub.Foreground")));s.Setters.Add(new Setter(Control.BorderBrushProperty,new DynamicResourceExtension("Hub.Border")));g.Resources[t]=s;}return g;}
        static void Column(DataGrid grid,string title,string property,double width){grid.Columns.Add(new DataGridTextColumn {Header=title,Binding=new Binding(property){TargetNullValue="—",StringFormat=(property=="CpuPercent"||property=="GpuPercent"||property=="MemoryMB"||property=="ReadMB"||property=="WriteMB")?"{0:0.0}":null},SortMemberPath=property=="Runtime"?"RuntimeSeconds":property,Width=width});}
        public override void ApplyConfig(MonitorConfig value){base.ApplyConfig(value);period.Text="История: "+value.HistorySeconds+" с · интервал "+value.IntervalSeconds+" с · нагрузка 0–100 %";}
        public void Update(Snapshot snapshot,Dictionary<string,List<double?>> history){
            foreach(var metric in snapshot.Metrics){MetricCard card;if(!cardMap.TryGetValue(metric.Id,out card)){card=new MetricCard(metric.Id,detach){Width=400};cardMap.Add(metric.Id,card);cards.Children.Add(card);}card.Update(metric,history,config);}
            foreach(var id in cardMap.Keys.Where(id=>!snapshot.Metrics.Any(m=>m.Id==id)).ToList()){cards.Children.Remove(cardMap[id]);cardMap.Remove(id);}ResizeCards();
            var list=snapshot.Processes.Select(p=>new ProcessRow(p)).ToList();SetItems(runtime,list);SetItems(resources,list);
            status.Text=snapshot.Timestamp.ToString("HH:mm:ss")+" · процессов: "+list.Count+(snapshot.Warnings!=null&&snapshot.Warnings.Count>0?" · "+String.Join("; ",snapshot.Warnings):"");status.ToolTip=status.Text;
        }
        void ResizeCards(){double available=cards.ActualWidth;if(available<1)return;int columns=Math.Max(1,(int)(available/350));double width=Math.Max(230,available/columns-14);foreach(var card in cardMap.Values)if(Math.Abs(card.Width-width)>1)card.Width=width;}
        static void SetItems(DataGrid grid,List<ProcessRow> values){var sorts=grid.Items.SortDescriptions.ToArray();grid.ItemsSource=values;grid.Items.SortDescriptions.Clear();if(sorts.Length==0&&grid.Columns.Any(c=>c.SortMemberPath=="CpuPercent"))grid.Items.SortDescriptions.Add(new SortDescription("CpuPercent",ListSortDirection.Descending));else foreach(var s in sorts)grid.Items.SortDescriptions.Add(s);}
        sealed class ProcessRow {
            public string Name {get;private set;} public int Pid {get;private set;} public string Background {get;private set;} public DateTime? Started {get;private set;} public string Runtime {get;private set;} public double RuntimeSeconds {get;private set;}
            public double? CpuPercent {get;private set;} public double? MemoryMB {get;private set;} public double? GpuPercent {get;private set;} public double? ReadMB {get;private set;} public double? WriteMB {get;private set;}
            internal ProcessRow(ProcessMetric p){Name=p.Name;Pid=p.Pid;Background=p.Background?"Да":"Нет";Started=p.Started.HasValue?(DateTime?)p.Started.Value.ToLocalTime():null;RuntimeSeconds=p.Runtime.TotalSeconds;Runtime=String.Format("{0} д {1:00}:{2:00}:{3:00}",(int)p.Runtime.TotalDays,p.Runtime.Hours,p.Runtime.Minutes,p.Runtime.Seconds);CpuPercent=p.CpuPercent;MemoryMB=p.MemoryBytes/1048576.0;GpuPercent=p.GpuPercent;ReadMB=p.ReadBytesPerSecond/1048576.0;WriteMB=p.WriteBytesPerSecond/1048576.0;}
        }
    }
}
