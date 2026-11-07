using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using ReFix.Shared;
using ReFix.TaskMenu;
namespace ReFix.MiniIcons
{
    internal sealed class Controller:IDisposable
    {
        sealed class Surface
        {
            internal MonitorInfo Monitor;internal EdgeWindow Edge;internal PopupWindow Popup;
        }
        readonly Application app;readonly ThemeStore themes;readonly ControlsStore controls;readonly Topology topology;readonly MiniIconsStore store;readonly List<Surface> surfaces=new List<Surface>();readonly FileSystemWatcher entriesWatcher;readonly DispatcherTimer refresh;
        readonly DispatcherTimer layerRefresh;
        readonly Native.WinEvent layerEvent;
        readonly IntPtr foregroundHook,windowHook,minimizeHook;
        readonly ExternalWindowsOverflow overflow=new ExternalWindowsOverflow();MiniIconsConfig config;IList<MiniTrayEntry> entries=new List<MiniTrayEntry>();SettingsWindow settings;bool disposed;
        Surface Primary{get{return surfaces.FirstOrDefault(s=>s.Monitor.Id==System.Windows.Forms.Screen.PrimaryScreen.DeviceName)??surfaces.FirstOrDefault();}}
        internal EdgeWindow Edge{get{return Primary==null?null:Primary.Edge;}}internal PopupWindow Popup{get{return Primary==null?null:Primary.Popup;}}
        internal Controller(Application app,ThemeStore themes,ControlsStore controls,Topology topology)
        {
            this.app=app;this.themes=themes;this.controls=controls;this.topology=topology;store=new MiniIconsStore(Paths.Root);config=store.Load();
            string directory=Path.Combine(MiniTrayFiles.TrayDirectory(Paths.Root),"entries");if(!MiniTrayFiles.SafePath(directory))throw new IOException("Unsafe tray entries path");Directory.CreateDirectory(directory);entriesWatcher=new FileSystemWatcher(directory,"*.json"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};entriesWatcher.Changed+=EntriesChanged;entriesWatcher.Created+=EntriesChanged;entriesWatcher.Deleted+=EntriesChanged;entriesWatcher.Renamed+=EntriesChanged;entriesWatcher.Error+=delegate{EntriesChanged(this,null);};
            refresh=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(70)};refresh.Tick+=delegate{refresh.Stop();if(!disposed&&surfaces.Any(s=>s.Popup.IsVisible))RefreshEntries();};
            themes.Changed+=ThemeChanged;controls.Changed+=ControlsChanged;topology.Changed+=TopologyChanged;
            layerRefresh=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(40)};
            layerRefresh.Tick+=delegate{layerRefresh.Stop();RefreshLayers();};
            layerEvent=OnLayerEvent;
            foregroundHook=Native.SetWinEventHook(3,3,IntPtr.Zero,layerEvent,0,0,2);
            minimizeHook=Native.SetWinEventHook(0x16,0x17,IntPtr.Zero,layerEvent,0,0,2);
            windowHook=Native.SetWinEventHook(0x8001,0x800B,IntPtr.Zero,layerEvent,0,0,2);
            Reposition();RefreshEntries();entriesWatcher.EnableRaisingEvents=true;
        }
        Surface CreateSurface(MonitorInfo monitor)
        {
            var s=new Surface{Monitor=monitor};
            s.Popup=new PopupWindow(themes.Current,Transfer,Reorder,ShowSettings,delegate{OpenExternal(s);});new WindowInteropHelper(s.Popup).EnsureHandle();
            s.Edge=new EdgeWindow(themes.Current,delegate{Toggle(s);},ShowSettings,delegate{app.Shutdown();},TransferWindow);s.Edge.Show();
            s.Popup.IsVisibleChanged+=delegate{UpdateEdgeVisibility(s);};ApplyBehavior(s);return s;
        }
        void Publish(){var primary=Primary;if(primary==null)MiniTrayFiles.RemoveHost(Paths.Root);else MiniTrayFiles.PublishHost(Paths.Root,primary.Edge.Handle,config.OwnTransfers,primary.Popup.Handle,surfaces.SelectMany(s=>new[]{s.Edge.Handle,s.Popup.Handle}));}
        void EntriesChanged(object sender,FileSystemEventArgs e){app.Dispatcher.BeginInvoke(new Action(delegate{if(disposed)return;if(surfaces.Any(s=>s.Popup.IsVisible)&&!refresh.IsEnabled)refresh.Start();}));}
        void RefreshEntries(){if(disposed)return;entries=MiniTrayFiles.ReadEntries(Paths.Root).Where(e=>config.OwnTransfers.Contains(e.Id)).ToList();foreach(var s in surfaces){s.Popup.Refresh(config,entries,themes.Current);s.Popup.Place(s.Monitor,s.Edge);}}
        Surface PointerSurface(){var p=System.Windows.Forms.Cursor.Position;return surfaces.FirstOrDefault(s=>s.Monitor.Bounds.Contains(new Point(p.X,p.Y)))??Primary;}
        internal void Toggle(){var s=PointerSurface();if(s!=null)Toggle(s);}
        void Toggle(Surface s){if(disposed)return;if(s.Popup.IsVisible&&!s.Popup.IsClosing)s.Popup.HideAnimated();else ShowPopup(s);}
        void UpdateEdgeVisibility(Surface s){if(disposed||!surfaces.Contains(s))return;if(s.Popup.IsVisible)s.Edge.Hide();else if(!s.Edge.IsVisible)s.Edge.Show();RefreshLayers();}
        internal void ShowPopup(){var s=PointerSurface();if(s!=null)ShowPopup(s);}
        void ShowPopup(Surface s){if(disposed)return;bool opening=!s.Popup.IsVisible||s.Popup.IsClosing;RefreshEntries();s.Popup.Show();s.Popup.UpdateLayout();s.Popup.Place(s.Monitor,s.Edge);if(opening)s.Popup.AnimateOpen(s.Monitor);s.Popup.Activate();RefreshLayers();}
        internal void ShowSettings(){if(disposed)return;if(settings==null){settings=new SettingsWindow(config,topology,themes.Current,Save,OpenHub);settings.Closed+=delegate{settings=null;};}settings.Show();settings.Activate();}
        void OpenHub(){var hub=MiniTrayFiles.ReadEntries(Paths.Root).FirstOrDefault(e=>e.Id=="hub");if(hub==null||!MiniTrayFiles.Invoke(hub,false,0,0))MessageBox.Show("Откройте HUB и выберите Настройки → Оформление → Настройка Мини значков.","Мини значки");}
        void TransferWindow(IntPtr source,bool restore){var entry=MiniTrayFiles.ReadEntries(Paths.Root).FirstOrDefault(e=>MiniTrayFiles.Handle(e)==source);if(entry!=null)Transfer(entry.Id,restore);}
        internal void Transfer(string id,bool restore){if(!MiniTrayFiles.ValidId(id))return;if(restore)config.OwnTransfers.Remove(id);else if(!config.OwnTransfers.Contains(id))config.OwnTransfers.Add(id);Save();if(!restore)ShowPopup();}
        void Reorder(IList<string> order){config.OwnOrder=order.Concat(config.OwnOrder.Where(id=>!order.Contains(id))).ToList();Save();}
        void Save(){try{store.Save(config);Publish();app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{if(!disposed){Reposition();RefreshEntries();}}));}catch(Exception ex){if(!(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException))throw;foreach(var s in surfaces)s.Popup.SetStatus("Не удалось сохранить: "+ex.Message);}}
        void Reposition()
        {
            if(disposed)return;
            var ids=config.Monitors??new List<string>();
            var selected=topology.Monitors.Where(m=>ids.Contains(m.Id)).ToList();
            if(ids.Count==0){var legacy=topology.Monitors.FirstOrDefault(m=>m.Id==config.Monitor);if(legacy!=null)selected.Add(legacy);}
            if(selected.Count==0){var fallback=topology.Monitors.FirstOrDefault(m=>m.Id==System.Windows.Forms.Screen.PrimaryScreen.DeviceName)??topology.Monitors.FirstOrDefault();if(fallback!=null)selected.Add(fallback);}
            foreach(var s in surfaces.Where(s=>!selected.Any(m=>m.Id==s.Monitor.Id)).ToList()){surfaces.Remove(s);s.Popup.Close();s.Edge.Close();}
            foreach(var monitor in selected){var s=surfaces.FirstOrDefault(x=>x.Monitor.Id==monitor.Id);if(s==null){s=CreateSurface(monitor);surfaces.Add(s);s.Popup.Refresh(config,entries,themes.Current);}s.Monitor=monitor;s.Edge.Place(monitor);s.Popup.Place(monitor,s.Edge);UpdateEdgeVisibility(s);}
            Publish();
        }
        void ApplyBehavior(Surface s){var p=controls.Current;s.Popup.AutoHide=p.AutoHideMiniIcons;s.Popup.HideGesture=p.Get("mini-icons.hide");}
        void OnLayerEvent(IntPtr hook,uint ev,IntPtr window,int obj,int child,uint thread,uint time)
        {
            if(disposed||obj!=0||child!=0||layerMode==TaskMenuLayer.AboveAll)return;
            if(ev!=3&&ev!=0x16&&ev!=0x17&&ev!=0x8001&&ev!=0x8002&&ev!=0x8003&&ev!=0x8004&&ev!=0x800B)return;
            if(!layerRefresh.IsEnabled)layerRefresh.Start();
        }
        TaskMenuLayer layerMode;
        bool IsOwnSurface(IntPtr window){return surfaces.Any(s=>s.Edge.Handle==window||s.Popup.Handle==window);}
        void RefreshLayers()
        {
            if(disposed)return;
            layerMode=controls.Current.MiniIconsLayer;
            IntPtr foreground=LayerNative.GetForegroundWindow();
            Native.RECT rect;
            Rect bounds=Rect.Empty;
            if(layerMode==TaskMenuLayer.ExceptFullscreen&&foreground!=IntPtr.Zero&&!IsOwnSurface(foreground)&&!DockStacking.IsDesktop(foreground)&&!LayerNative.IsIconic(foreground)&&Native.GetWindowRect(foreground,out rect))bounds=rect.ToRect();
            bool maximized=!bounds.IsEmpty&&LayerNative.IsZoomed(foreground);
            foreach(var s in surfaces)
            {
                bool fullscreen=layerMode==TaskMenuLayer.ExceptFullscreen&&DockStacking.FullscreenOnMonitor(s.Monitor.Bounds,bounds,maximized,IsOwnSurface);
                bool top=TaskMenuLayerPolicy.Topmost(layerMode,fullscreen);
                ApplyLayer(s.Edge,s.Edge.Handle,top);
                ApplyLayer(s.Popup,s.Popup.Handle,top);
            }
        }
        static void ApplyLayer(Window window,IntPtr handle,bool top)
        {
            if(window.Topmost!=top)window.Topmost=top;
            if(window.IsVisible&&!top)DockStacking.Apply(handle,false);
        }
        void ThemeChanged(object sender,EventArgs e){app.Dispatcher.BeginInvoke(new Action(delegate{if(disposed)return;foreach(var s in surfaces){s.Edge.ApplyTheme(themes.Current);s.Popup.ApplyTheme(themes.Current);}RefreshEntries();if(settings!=null)settings.ApplyTheme(themes.Current);}));}
        void ControlsChanged(object sender,EventArgs e){app.Dispatcher.BeginInvoke(new Action(delegate{if(disposed)return;foreach(var s in surfaces)ApplyBehavior(s);RefreshLayers();}));}
        void TopologyChanged(object sender,EventArgs e){app.Dispatcher.BeginInvoke(new Action(delegate{if(disposed)return;Reposition();if(settings!=null){settings.Close();ShowSettings();}}));}
        async void OpenExternal(Surface s){s.Popup.SetStatus("Открытие системной области значков Windows…");bool result=await overflow.OpenWindowsOverflowAsync();if(disposed||!surfaces.Contains(s))return;if(result)s.Popup.HideAnimated();else s.Popup.SetStatus(overflow.Detail);}
        public void Dispose(){if(disposed)return;disposed=true;layerRefresh.Stop();foreach(var hook in new[]{foregroundHook,minimizeHook,windowHook})if(hook!=IntPtr.Zero)Native.UnhookWinEvent(hook);MiniTrayFiles.RemoveHost(Paths.Root);entriesWatcher.EnableRaisingEvents=false;entriesWatcher.Dispose();refresh.Stop();themes.Changed-=ThemeChanged;controls.Changed-=ControlsChanged;topology.Changed-=TopologyChanged;overflow.Dispose();if(settings!=null)settings.Close();foreach(var s in surfaces){s.Popup.Close();s.Edge.Close();}surfaces.Clear();}
    }
}
