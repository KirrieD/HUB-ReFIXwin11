#if SYSTEM_MONITOR_FIXTURE
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReFix.Shared;
using ReFix.SystemMonitor;

namespace ReFix.Tests
{
    internal static class SystemMonitorTests
    {
        static int checks;
        static void Check(string name,bool ok){if(!ok)throw new Exception(name);checks++;Console.WriteLine("PASS "+name);}
        static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
        static T Find<T>(DependencyObject parent,Predicate<T> match)where T:DependencyObject
        {
            var value=parent as T;if(value!=null&&match(value))return value;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(parent);i++){var found=Find(VisualTreeHelper.GetChild(parent,i),match);if(found!=null)return found;}
            return null;
        }
        static void Capture(Window window,string path)
        {
            window.UpdateLayout();
            var bitmap=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);
            bitmap.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));
            using(var output=File.Create(path))png.Save(output);
        }
        [STAThread]static int Main()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"monitor-test-"+Guid.NewGuid().ToString("N"));
            try
            {
                var store=new ConfigStore(root);var config=store.Load();config.Compact=new WindowPlacement{X=-320,Y=10,Positioned=true};config.Opacity=.75;config.Colors["cpu"]="#123456";
                store.Save(config);var loaded=store.Load();
                Check("configuration retains physical monitor position and custom appearance",loaded.Compact.X==-320&&loaded.Colors["cpu"]=="#123456"&&loaded.Opacity==.75);
                loaded.IntervalSeconds=0;loaded.HistorySeconds=99999;loaded.Scale=Double.NaN;loaded.Normalize();
                Check("configuration normalizes sampling and history bounds",loaded.IntervalSeconds==1&&loaded.HistorySeconds==1200&&loaded.Scale==1);
                var now=DateTime.UtcNow;
                var timeline=Controller.Timeline(new Dictionary<string,List<double?>>{{"cpu",new List<double?>{10,20,30}}},new Dictionary<string,List<DateTime>>{{"cpu",new List<DateTime>{now.AddSeconds(-10),now.AddSeconds(-5),now}}},now,30,1)["cpu"];
                Check("history preserves elapsed spacing and leaves unmeasured slots empty",timeline.Count==31&&timeline[20]==10&&timeline[25]==20&&timeline[30]==30&&!timeline[26].HasValue);
                Snapshot sample;
                using(var collector=new Collector())
                {
                    collector.Collect(true);Thread.Sleep(1100);
                    var watch=Stopwatch.StartNew();sample=collector.Collect(true);watch.Stop();
                    Check("native memory and CPU metrics are bounded",sample.Metrics.Any(m=>m.Kind=="memory"&&m.TotalBytes>0)&&sample.Metrics.All(m=>!m.Percent.HasValue||(m.Percent>=0&&m.Percent<=100)));
                    Check("process view includes this process",sample.Processes.Any(p=>p.Pid==Process.GetCurrentProcess().Id));
                    Check("runtime identities distinguish PID reuse",sample.Processes.Where(p=>p.Started.HasValue).All(p=>p.Id.Contains(":")));
                    Check("hidden collection does not enumerate process rows",collector.Collect(false).Processes.Count==0);
                    Console.WriteLine("Telemetry second sample "+watch.ElapsedMilliseconds+" ms; "+sample.Metrics.Count+" metrics; "+sample.Processes.Count+" processes");
                }
                var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};
                var theme=new ThemeProfile{Mode="Custom",Background="#202020",Foreground="#FFFFFF"};
                int opened=0;
                var histories=sample.Metrics.ToDictionary(m=>m.Id,m=>Enumerable.Range(0,60).Select(i=>(double?)Math.Max(0,Math.Min(100,(m.Percent??20)+Math.Sin(i*.7)*3+Math.Cos(i*.21)*2))).ToList());
                var compact=new CompactWindow(theme,()=>opened++,()=>{},()=>{});compact.ApplyConfig(config);compact.Show();compact.Update(sample,histories);Pump();
                Check("desktop summary is compact",compact.Width<=350&&compact.Height<=280&&!compact.ShowInTaskbar);
                var spark=Find<Sparkline>(compact,s=>true);
                Check("summary charts use readable area lines and initial history fills available width",spark!=null&&spark.ActualWidth>=65&&spark.ActualHeight>=20&&spark.FillArea&&spark.TrimLeadingMissing);
                spark.FitRange=false;
                spark.TrimLeadingMissing=false;
                spark.Update(new double?[]{null,50,null,null,50,null});
                Pump();
                var drawings=VisualTreeHelper.GetDrawing(spark).Children.OfType<GeometryDrawing>().ToArray();
                var midpoint=new Point(spark.ActualWidth/2,spark.ActualHeight/2);
                var below=new Point(midpoint.X,spark.ActualHeight*.8);
                Check("line and area bridge missing history slots",
                    drawings.Any(d=>d.Pen!=null&&d.Geometry.StrokeContains(d.Pen,midpoint))&&
                    drawings.Any(d=>d.Brush!=null&&d.Geometry.FillContains(below)));
                Check("chart does not extend beyond the measured interval",
                    drawings.All(d=>d.Geometry.Bounds.IsEmpty||d.Geometry.Bounds.Left>0&&d.Geometry.Bounds.Right<spark.ActualWidth));
                compact.Update(sample,histories);Pump();
                var texts=Find<StackPanel>(compact,p=>p.Children.OfType<Grid>().Count()==4);
                Check("summary uses four aligned single-line metric rows",texts!=null&&texts.Children.OfType<Grid>().All(g=>g.ColumnDefinitions.Count==4));

                var row=Find<Grid>(compact,g=>g.Cursor==Cursors.Hand);
                Check("summary has clickable metric row",row!=null);
                row.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=UIElement.MouseLeftButtonUpEvent});
                Check("summary opens full panel on click",opened==1);
                Capture(compact,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"system-monitor-compact.png"));
                var main=new MainWindow(theme,()=>{},id=>{});main.ApplyConfig(config);main.Show();main.Update(sample,histories);Pump();
                Check("main panel includes performance runtime and consumers",Find<TabControl>(main,t=>t.Items.Count==3)!=null);
                Find<TabControl>(main,t=>t.Items.Count==3).SelectedIndex=2;Pump();
                var grid=Find<DataGrid>(main,g=>g.Columns.Count>=6);
                Check("resource consumers expose measured columns",grid!=null&&grid.Items.Count>0);
                double nameWidth=grid.Columns[0].ActualWidth;
                main.Width+=150;main.UpdateLayout();Pump();
                Check("process name column uses extra space instead of an empty gutter",grid.Columns[0].ActualWidth>nameWidth+100);
                Find<TabControl>(main,t=>t.Items.Count==3).SelectedIndex=0;Pump();
                main.ApplyTheme(new ThemeProfile{Mode="Custom",Background="#FFFFFF",Foreground="#111111"});main.Update(sample,histories);Pump();
                Capture(main,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"system-monitor-main-light.png"));
                main.Close();compact.Close();
                string prior=Environment.GetEnvironmentVariable("REFIX_DATA_ROOT");
                Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",root);
                try
                {
                    using(var themes=new ThemeStore(root))
                    using(var controller=new Controller(app,themes))
                    {
                        var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                        var summary=(Window)typeof(Controller).GetField("compact",flags).GetValue(controller);
                        var timer=(DispatcherTimer)typeof(Controller).GetField("timer",flags).GetValue(controller);
                        Check("module launch opens desktop summary without main panel",summary.IsVisible&&typeof(Controller).GetField("main",flags).GetValue(controller)==null);
                        var draft=new MonitorConfig{Scale=1.25};
                        typeof(Controller).GetMethod("SaveSettings",flags).Invoke(controller,new object[]{draft});
                        draft.Scale=2;
                        Check("applied settings own a snapshot independent from later edits",((MonitorConfig)typeof(Controller).GetField("config",flags).GetValue(controller)).Scale==1.25);
                        controller.Toggle();Pump();
                        Check("all hidden windows stop the scheduler without background history",!summary.IsVisible&&!timer.IsEnabled);
                        controller.ShowMain();Pump();
                        var full=(Window)typeof(Controller).GetField("main",flags).GetValue(controller);
                        full.Close();Pump();
                        Check("closing main panel restores desktop summary",summary.IsVisible&&!full.IsVisible);
                    }
                }
                finally{Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",prior);}
                app.Shutdown();
                Console.WriteLine(checks+" monitor checks passed");return 0;
            }
            catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}

#endif
