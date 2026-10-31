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
        readonly Application app;readonly ThemeStore themes;readonly ControlsStore controls;readonly Topology topology;readonly MiniIconsStore store;readonly EdgeWindow edge;readonly PopupWindow popup;readonly FileSystemWatcher entriesWatcher;readonly DispatcherTimer refresh;
        readonly ExternalWindowsOverflow overflow=new ExternalWindowsOverflow();MiniIconsConfig config;IList<MiniTrayEntry> entries=new List<MiniTrayEntry>();MonitorInfo monitor;SettingsWindow settings;bool disposed;
        internal EdgeWindow Edge{get{return edge;}}internal PopupWindow Popup{get{return popup;}}
        internal Controller(Application app,ThemeStore themes,ControlsStore controls,Topology topology)
        {
            this.app=app;this.themes=themes;this.controls=controls;this.topology=topology;store=new MiniIconsStore(Paths.Root);config=store.Load();
            popup=new PopupWindow(themes.Current,Transfer,Reorder,ShowSettings,OpenExternal);new WindowInteropHelper(popup).EnsureHandle();
            edge=new EdgeWindow(themes.Current,Toggle,ShowSettings,delegate{app.Shutdown();},TransferWindow);edge.Show();Reposition();
            popup.IsVisibleChanged+=PopupVisibilityChanged;
            string directory=Path.Combine(MiniTrayFiles.TrayDirectory(Paths.Root),"entries");if(!MiniTrayFiles.SafePath(directory))throw new IOException("Unsafe tray entries path");Directory.CreateDirectory(directory);entriesWatcher=new FileSystemWatcher(directory,"*.json"){NotifyFilter=NotifyFilters.FileName|NotifyFilters.LastWrite|NotifyFilters.Size};entriesWatcher.Changed+=EntriesChanged;entriesWatcher.Created+=EntriesChanged;entriesWatcher.Deleted+=EntriesChanged;entriesWatcher.Renamed+=EntriesChanged;entriesWatcher.Error+=delegate{EntriesChanged(this,null);};entriesWatcher.EnableRaisingEvents=true;
            refresh=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(70)};refresh.Tick+=delegate{refresh.Stop();if(!disposed&&popup.IsVisible)RefreshEntries();};
            themes.Changed+=ThemeChanged;controls.Changed+=ControlsChanged;topology.Changed+=TopologyChanged;
            ApplyBehavior();Publish();RefreshEntries();
        }
        void Publish(){MiniTrayFiles.PublishHost(Paths.Root,edge.Handle,config.OwnTransfers,popup.Handle);}
        void EntriesChanged(object sender,FileSystemEventArgs e){app.Dispatcher.BeginInvoke(new Action(delegate{if(disposed)return;if(popup.IsVisible&&!refresh.IsEnabled)refresh.Start();}));}
        void RefreshEntries(){if(disposed)return;entries=MiniTrayFiles.ReadEntries(Paths.Root).Where(e=>config.OwnTransfers.Contains(e.Id)).ToList();popup.Refresh(config,entries,themes.Current);if(popup.IsVisible&&monitor!=null)popup.Place(monitor,edge);}
        internal void Toggle(){if(popup.IsVisible)popup.Hide();else ShowPopup();}
        void PopupVisibilityChanged(object sender,DependencyPropertyChangedEventArgs args){UpdateEdgeVisibility();}
        void UpdateEdgeVisibility(){if(disposed)return;if(monitor==null||popup.IsVisible)edge.Hide();else if(!edge.IsVisible)edge.Show();}
        internal void ShowPopup(){if(disposed)return;RefreshEntries();popup.Show();popup.UpdateLayout();if(monitor!=null)popup.Place(monitor,edge);popup.Activate();}
        internal void ShowSettings(){if(disposed)return;if(settings==null){settings=new SettingsWindow(config,topology,themes.Current,Save,OpenHub);settings.Closed+=delegate{settings=null;};}settings.Show();settings.Activate();}
        void OpenHub(){var hub=MiniTrayFiles.ReadEntries(Paths.Root).FirstOrDefault(e=>e.Id=="hub");if(hub==null||!MiniTrayFiles.Invoke(hub,false,0,0))MessageBox.Show("Откройте HUB и выберите Настройки → Оформление → Настройка Мини значков.","Мини значки");}
        void TransferWindow(IntPtr source,bool restore){var entry=MiniTrayFiles.ReadEntries(Paths.Root).FirstOrDefault(e=>MiniTrayFiles.Handle(e)==source);if(entry!=null)Transfer(entry.Id,restore);}
        internal void Transfer(string id,bool restore){if(!MiniTrayFiles.ValidId(id))return;if(restore)config.OwnTransfers.Remove(id);else if(!config.OwnTransfers.Contains(id))config.OwnTransfers.Add(id);Save();if(!restore)ShowPopup();}
        void Reorder(IList<string> order){config.OwnOrder=order.Concat(config.OwnOrder.Where(id=>!order.Contains(id))).ToList();Save();}
        void Save(){try{store.Save(config);Publish();app.Dispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{if(!disposed){RefreshEntries();Reposition();}}));}catch(Exception ex){if(!(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException))throw;popup.SetStatus("Не удалось сохранить: "+ex.Message);}}
        void Reposition(){if(disposed)return;monitor=topology.Monitors.FirstOrDefault(m=>m.Id==config.Monitor)??topology.Monitors.FirstOrDefault(m=>m.Id==System.Windows.Forms.Screen.PrimaryScreen.DeviceName)??topology.Monitors.FirstOrDefault();if(monitor==null){edge.Hide();popup.Hide();MiniTrayFiles.RemoveHost(Paths.Root);return;}edge.Place(monitor);UpdateEdgeVisibility();if(popup.IsVisible)popup.Place(monitor,edge);}
        void ApplyBehavior(){var p=controls.Current;popup.AutoHide=p.AutoHideMiniIcons;popup.HideGesture=p.Get("mini-icons.hide");}
        void ThemeChanged(object sender,EventArgs e){app.Dispatcher.BeginInvoke(new Action(delegate{if(disposed)return;edge.ApplyTheme(themes.Current);popup.ApplyTheme(themes.Current);if(popup.IsVisible)RefreshEntries();if(settings!=null)settings.ApplyTheme(themes.Current);}));}
        void ControlsChanged(object sender,EventArgs e){app.Dispatcher.BeginInvoke(new Action(delegate{if(!disposed)ApplyBehavior();}));}
        void TopologyChanged(object sender,EventArgs e){app.Dispatcher.BeginInvoke(new Action(delegate{if(disposed)return;Reposition();if(monitor!=null)Publish();if(settings!=null){settings.Close();settings=null;ShowSettings();}}));}
        async void OpenExternal(){popup.SetStatus("Открытие системной области значков Windows…");bool result=await overflow.OpenWindowsOverflowAsync();if(disposed)return;if(result)popup.Hide();else popup.SetStatus(overflow.Detail);}
        public void Dispose(){if(disposed)return;disposed=true;MiniTrayFiles.RemoveHost(Paths.Root);entriesWatcher.EnableRaisingEvents=false;entriesWatcher.Dispose();refresh.Stop();themes.Changed-=ThemeChanged;controls.Changed-=ControlsChanged;topology.Changed-=TopologyChanged;overflow.Dispose();if(settings!=null)settings.Close();popup.Close();edge.Close();}
    }
}
