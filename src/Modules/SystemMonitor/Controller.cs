using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Win32;
using ReFix.Shared;

namespace ReFix.SystemMonitor
{
    internal sealed class Controller:IDisposable
    {
        readonly Application app;
        readonly ThemeStore themes;
        readonly ConfigStore store;
        readonly Collector collector=new Collector();
        readonly DispatcherTimer timer=new DispatcherTimer();
        readonly Dictionary<string,List<double?>> history=new Dictionary<string,List<double?>>();
        readonly Dictionary<string,List<DateTime>> times=new Dictionary<string,List<DateTime>>();
        readonly Dictionary<string,ChartWindow> charts=new Dictionary<string,ChartWindow>();
        readonly CompactWindow compact;
        MainWindow main;
        SettingsWindow settings;
        MonitorConfig config;
        Snapshot snapshot;
        Task<Snapshot> pending;
        bool disposed, stopping;
        DateTime lastSample=DateTime.MinValue;

        internal Controller(Application app,ThemeStore themes)
        {
            this.app=app;this.themes=themes;store=new ConfigStore(Paths.Root);config=store.Load();
            compact=new CompactWindow(themes.Current,ShowMain,ShowSettings,SaveCompact);
            compact.ApplyConfig(config);compact.IsVisibleChanged+=delegate{UpdateTimer();};
            compact.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!disposed){e.Cancel=true;compact.Hide();}};
            themes.Changed+=ThemeChanged;
            SystemEvents.DisplaySettingsChanged+=DisplayChanged;
            timer.Interval=TimeSpan.FromSeconds(1);timer.Tick+=Tick;
            // A launched monitor opens its desktop summary. Background restores no main panel.
            Show();UpdateTimer();Sample();
        }
        internal void Show(){if(disposed)return;compact.Show();Restore(compact,config.Compact);Sample();}
        internal void Toggle(){if(disposed)return;if(compact.IsVisible)compact.Hide();else Show();}
        internal void ShowMain()
        {
            if(disposed)return;
            if(main==null)
            {
                main=new MainWindow(themes.Current,ShowSettings,Detach);
                main.ApplyConfig(config);
                main.IsVisibleChanged+=delegate{UpdateTimer();};
                main.Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e){if(!disposed){e.Cancel=true;main.Hide();Show();}};
            }
            main.Show();main.Activate();Render();Sample();
        }
        internal void ShowSettings()
        {
            if(disposed)return;
            if(settings==null)
            {
                settings=new SettingsWindow(themes.Current,config,SaveSettings);
                settings.Closed+=delegate{settings=null;};
            }
            settings.Show();settings.Activate();
        }
        void SaveSettings(MonitorConfig value)
        {
            value=new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<MonitorConfig>(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(value));
            value.Compact=config.Compact;value.Charts=config.Charts;
            store.Save(value);config=value;
            compact.ApplyConfig(config);
            if(main!=null)main.ApplyConfig(config);
            foreach(var chart in charts.Values)chart.ApplyConfig(config);
            UpdateTimer();Trim();Render();
        }
        void Detach(string id)
        {
            if(disposed)return;
            ChartWindow chart;
            if(!charts.TryGetValue(id,out chart))
            {
                chart=new ChartWindow(themes.Current,id,delegate{SaveChart(id);});
                chart.ApplyConfig(config);
                charts.Add(id,chart);
                chart.IsVisibleChanged+=delegate{UpdateTimer();};
                chart.Closed+=delegate{charts.Remove(id);UpdateTimer();};
            }
            chart.Show();
            WindowPlacement place;
            if(config.Charts.TryGetValue(id,out place)){if(place.Width>=250&&place.Width<=1600)chart.Width=place.Width;if(place.Height>=180&&place.Height<=1200)chart.Height=place.Height;Restore(chart,place);}
            Render();
        }
        void SaveCompact(){Capture(compact,config.Compact);SavePlacement();}
        void SaveChart(string id)
        {
            ChartWindow chart;if(!charts.TryGetValue(id,out chart))return;
            WindowPlacement place;if(!config.Charts.TryGetValue(id,out place)){place=new WindowPlacement();config.Charts[id]=place;}
            Capture(chart,place);SavePlacement();
        }
        void SavePlacement()
        {
            try{store.Save(config);}
            catch(Exception ex){compact.ToolTip="Не удалось сохранить положение: "+ex.Message;}
        }
        bool Visible{get{return compact.IsVisible||(main!=null&&main.IsVisible)||charts.Values.Any(c=>c.IsVisible);}}
        void UpdateTimer(){if(disposed)return;if(Visible||config.BackgroundHistory)timer.Start();else timer.Stop();}
        void Tick(object sender,EventArgs e){Sample();}
        async void Sample()
        {
            if(disposed||pending!=null)return;
            int interval=Visible?config.IntervalSeconds:config.BackgroundHistory?Math.Max(5,config.IntervalSeconds):0;
            if(interval==0||DateTime.UtcNow-lastSample<TimeSpan.FromSeconds(interval))return;
            bool processes=(main!=null&&main.IsVisible);
            lastSample=DateTime.UtcNow;
            pending=Task.Run(()=>collector.Collect(processes));
            try
            {
                var value=await pending;
                if(disposed)return;
                snapshot=value;
                foreach(var metric in value.Metrics)
                {
                    List<double?> values;
                    if(!history.TryGetValue(metric.Id,out values)){values=new List<double?>();history[metric.Id]=values;times[metric.Id]=new List<DateTime>();}
                    values.Add(metric.Percent);times[metric.Id].Add(DateTime.UtcNow);
                }
                foreach(var key in history.Keys.Where(id=>!value.Metrics.Any(m=>m.Id==id)).ToArray()){history.Remove(key);times.Remove(key);}
                Trim();Render();
            }
            catch(Exception ex){if(!disposed)compact.ToolTip="Не удалось получить показатели: "+ex.Message;}
            finally{pending=null;if(stopping)collector.Dispose();}
        }
        void Trim()
        {
            var cutoff=DateTime.UtcNow.AddSeconds(-config.HistorySeconds);
            foreach(var pair in history)
            {
                var stamps=times[pair.Key];int count=0;
                while(count<stamps.Count&&stamps[count]<cutoff)count++;
                if(count>0){stamps.RemoveRange(0,count);pair.Value.RemoveRange(0,count);}
            }
        }
        void Render()
        {
            if(disposed||snapshot==null)return;
            var timeline=Timeline(history,times,DateTime.UtcNow,config.HistorySeconds,config.IntervalSeconds);
            if(compact.IsVisible)compact.Update(snapshot,timeline);
            if(main!=null&&main.IsVisible)main.Update(snapshot,timeline);
            foreach(var chart in charts.Values)if(chart.IsVisible)chart.Update(snapshot,timeline);
        }
        internal static Dictionary<string,List<double?>> Timeline(Dictionary<string,List<double?>> values,Dictionary<string,List<DateTime>> timestamps,DateTime now,int seconds,int interval)
        {
            int count=seconds/interval+1;
            var output=new Dictionary<string,List<double?>>();
            foreach(var pair in values)
            {
                var row=Enumerable.Repeat<double?>(null,count).ToList();
                var stamps=timestamps[pair.Key];
                for(int i=0;i<pair.Value.Count;i++)
                {
                    int offset=(int)Math.Round((now-stamps[i]).TotalSeconds/interval);
                    int index=count-1-offset;
                    if(index>=0&&index<count)row[index]=pair.Value[i];
                }
                output.Add(pair.Key,row);
            }
            return output;
        }
        void ThemeChanged(object sender,EventArgs e)
        {
            if(app.Dispatcher.HasShutdownStarted)return;
            app.Dispatcher.BeginInvoke(new Action(delegate
            {
                if(disposed)return;compact.ApplyTheme(themes.Current);
                if(main!=null)main.ApplyTheme(themes.Current);
                if(settings!=null)settings.ApplyTheme(themes.Current);
                foreach(var chart in charts.Values)chart.ApplyTheme(themes.Current);
            }));
        }
        void DisplayChanged(object sender,EventArgs e)
        {
            if(app.Dispatcher.HasShutdownStarted)return;
            app.Dispatcher.BeginInvoke(new Action(delegate
            {
                if(disposed)return;
                if(compact.IsVisible)Restore(compact,config.Compact);
                foreach(var item in charts){WindowPlacement place;if(config.Charts.TryGetValue(item.Key,out place)&&item.Value.IsVisible)Restore(item.Value,place);}
            }));
        }
        internal static void Capture(Window window,WindowPlacement place)
        {
            RECT bounds;
            if(!GetWindowRect(new WindowInteropHelper(window).Handle,out bounds))return;
            place.X=bounds.Left;place.Y=bounds.Top;place.Width=window.Width;place.Height=window.Height;place.Positioned=true;
        }
        internal static void Restore(Window window,WindowPlacement place)
        {
            var screens=System.Windows.Forms.Screen.AllScreens;if(screens.Length==0)return;
            var primary=System.Windows.Forms.Screen.PrimaryScreen??screens[0];
            int x=place.Positioned?place.X:primary.WorkingArea.Right-350;
            int y=place.Positioned?place.Y:primary.WorkingArea.Top+30;
            var screen=screens.FirstOrDefault(s=>s.Bounds.Contains(x,y))??primary;
            RECT bounds;if(!GetWindowRect(new WindowInteropHelper(window).Handle,out bounds))return;
            x=Math.Max(screen.WorkingArea.Left,Math.Min(x,screen.WorkingArea.Right-(bounds.Right-bounds.Left)));
            y=Math.Max(screen.WorkingArea.Top,Math.Min(y,screen.WorkingArea.Bottom-(bounds.Bottom-bounds.Top)));
            SetWindowPos(new WindowInteropHelper(window).Handle,IntPtr.Zero,x,y,0,0,0x15);
        }
        public void Dispose()
        {
            if(disposed)return;disposed=true;stopping=true;timer.Stop();
            themes.Changed-=ThemeChanged;SystemEvents.DisplaySettingsChanged-=DisplayChanged;
            compact.Close();if(main!=null)main.Close();if(settings!=null)settings.Close();
            foreach(var chart in charts.Values.ToArray())chart.Close();
            if(pending==null)collector.Dispose();
        }
        [StructLayout(LayoutKind.Sequential)]struct RECT{internal int Left,Top,Right,Bottom;}
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
        [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
    }
}
