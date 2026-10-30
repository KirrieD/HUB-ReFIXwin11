#if TASK_MENU_GRID_UI_FIXTURE
using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ReFix.Shared;
using ReFix.TaskMenu;
namespace ReFix.Tests
{
    // Compile with TaskMenu sources and /define:TASK_MENU_GRID_UI_FIXTURE /main:ReFix.Tests.TaskMenuGridUiTests.
    internal static class TaskMenuGridUiTests
    {
        [DllImport("user32.dll")] static extern bool SetCursorPos(int x,int y);
        [DllImport("user32.dll")] static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
        [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
        [DllImport("ole32.dll")] static extern void ReleaseStgMedium(ref System.Runtime.InteropServices.ComTypes.STGMEDIUM medium);
        static int failures;
        static void Check(string name,bool ok){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failures++;}
        static object Field(object target,string name){return target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);}
        static void Set(object target,string name,object value){target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);}
        static void Pump(int ms){var end=DateTime.UtcNow.AddMilliseconds(ms);do{var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);Thread.Sleep(5);}while(DateTime.UtcNow<end);}
        static DragEventArgs Drag(DockWindow dock,Canvas board,IDataObject data,RoutedEvent routed,int column,int row)
        {
            double cell=TaskMenuGrid.CellSize((TaskMenuPanel)Field(dock,"panel"));Point at=board.TranslatePoint(new Point(column*cell+cell/2,row*cell+cell/2),dock);
            var ctor=typeof(DragEventArgs).GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic)[0];var args=(DragEventArgs)ctor.Invoke(new object[]{data,DragDropKeyStates.LeftMouseButton,DragDropEffects.Move|DragDropEffects.Copy,dock,at});args.RoutedEvent=routed;dock.RaiseEvent(args);return args;
        }
        static T Find<T>(DependencyObject root,Predicate<T> match) where T:DependencyObject
        {
            var value=root as T;if(value!=null&&match(value))return value;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=Find<T>(VisualTreeHelper.GetChild(root,i),match);if(found!=null)return found;}return null;
        }
        static void SavePreview(Window window,string name)
        {
            var image=new RenderTargetBitmap((int)Math.Ceiling(window.ActualWidth),(int)Math.Ceiling(window.ActualHeight),96,96,PixelFormats.Pbgra32);image.Render(window);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using(var output=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,name)))png.Save(output);
        }
        static void LayerChecks(string root,ControlsStore controls,HwndSource source,MonitorInfo monitor)
        {
            var panel=new TaskMenuPanel{Monitor=monitor.Id,Edge=TaskMenuEdge.Right};var dock=new DockWindow(panel,monitor,new ThemeProfile{Mode="Custom"},controls,delegate{},delegate{},false,root);
            var cover=new Window{Title="ReFix layer fixture",Width=500,Height=300,WindowStartupLocation=WindowStartupLocation.Manual,Left=monitor.Work.Left+40,Top=monitor.Work.Top+40};
            dock.Show();cover.Show();Pump(60);IntPtr handle=new WindowInteropHelper(cover).Handle;
            using(var environment=new DockEnvironment(source,controls,new[]{dock}))
            {
                var p=controls.Current;p.TaskMenuLayer=TaskMenuLayer.AboveAll;p.ShowTaskMenuEdges=true;controls.Save(p);dock.BehaviorChanged();environment.RefreshForWindow(handle);
                Check("standard menu stays topmost and does not observe global mouse",DockPresentation.IsTopmost(dock.NativeHandle)&&!environment.RawEnabled);
                p.TaskMenuLayer=TaskMenuLayer.BelowWindows;controls.Save(p);dock.BehaviorChanged();environment.RefreshForWindow(handle);
                Check("below windows mode removes native topmost flag",!dock.Topmost&&!DockPresentation.IsTopmost(dock.NativeHandle)&&environment.RawEnabled);
                var ordered=new System.Collections.Generic.List<IntPtr>();Native.EnumWindows(delegate(IntPtr h,IntPtr unused){ordered.Add(h);return true;},IntPtr.Zero);
                Check("ordinary window appears before menu in native Z order",ordered.IndexOf(handle)>=0&&ordered.IndexOf(handle)<ordered.IndexOf(dock.NativeHandle));
                p.TaskMenuLayer=TaskMenuLayer.ExceptFullscreen;controls.Save(p);dock.BehaviorChanged();environment.RefreshForWindow(handle);Check("partial mode remains topmost above ordinary windows",DockPresentation.IsTopmost(dock.NativeHandle));
                IntPtr desktopHost=LayerNative.GetShellWindow();Check("desktop host is excluded from fullscreen application candidates",desktopHost==IntPtr.Zero||DockStacking.IsDesktop(desktopHost));
                cover.WindowState=WindowState.Maximized;Pump(60);environment.RefreshForWindow(handle);Check("partial mode yields to maximized native window",!DockPresentation.IsTopmost(dock.NativeHandle));
                cover.WindowState=WindowState.Normal;cover.WindowStyle=WindowStyle.None;cover.Left=monitor.Bounds.Left/monitor.Scale;cover.Top=monitor.Bounds.Top/monitor.Scale;cover.Width=monitor.Bounds.Width/monitor.Scale;cover.Height=monitor.Bounds.Height/monitor.Scale;Pump(60);environment.RefreshForWindow(handle);Check("partial mode yields to borderless monitor sized native window",!DockPresentation.IsTopmost(dock.NativeHandle));
                Check("fullscreen application remains respected when focus moves to another monitor",environment.FullscreenOnMonitor(monitor.Bounds,new Rect(monitor.Bounds.Right+50,monitor.Bounds.Top,400,300),false));
                p.TaskMenuLayer=TaskMenuLayer.AboveAll;p.ShowTaskMenuEdges=false;controls.Save(p);dock.BehaviorChanged();environment.RefreshForWindow(handle);dock.SetExpanded(false,true);
                Check("hidden edges draw no pixels and retain raw mouse observation",dock.Opacity==0&&((Border)Field(dock,"edge")).Visibility!=Visibility.Visible&&environment.RawEnabled);
                Rect full=(Rect)Field(dock,"full");environment.ObservePointer(new Point(full.Right-1,full.Top+full.Height/2));
                Check("invisible edge opens menu over covering window",dock.IsExpanded&&dock.Opacity==1&&DockPresentation.IsTopmost(dock.NativeHandle));
                p.ShowTaskMenuEdges=true;controls.Save(p);dock.BehaviorChanged();environment.RefreshForWindow(handle);dock.SetExpanded(false,true);
                Check("restoring visible edges restores sliver and unregisters raw mouse",dock.Opacity==1&&((Border)Field(dock,"edge")).Visibility==Visibility.Visible&&!environment.RawEnabled);
            }
            dock.Shutdown();cover.Close();var defaults=controls.Current;defaults.TaskMenuLayer=TaskMenuLayer.AboveAll;defaults.ShowTaskMenuEdges=true;controls.Save(defaults);
        }
        [STAThread] static int Main()
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"grid-ui-"+Guid.NewGuid().ToString("N"));
            try{using(var controls=new ControlsStore(root))using(var source=new HwndSource(new HwndSourceParameters("Task grid fixture"){Width=0,Height=0,WindowStyle=unchecked((int)0x80000000)}))using(var topology=new Topology(source))
            {
                var monitor=topology.Monitors[0];var p=new TaskMenuPanel{Monitor=monitor.Id,Edge=TaskMenuEdge.Left,IconSize=24};p.Items.Add(Path.Combine(root,"one.txt"));p.Items.Add(Path.Combine(root,"two.txt"));TaskMenuGrid.Ensure(p);TaskMenuGrid.Configure(p,4,3);TaskMenuGrid.Move(p,p.Items[1],3,2);
                int changed=0;var dock=new DockWindow(p,monitor,new ThemeProfile{Mode="Custom"},controls,delegate{changed++;},delegate{},false,root);dock.Show();Pump(80);dock.SetExpanded(true,true);Pump(80);
                IntPtr dockHandle=new WindowInteropHelper(dock).Handle;Check("dock is a tool window outside taskbar and Alt-Tab",DockPresentation.IsToolWindow(dockHandle));
                bool composed;uint affinity;if(DockPresentation.DwmIsCompositionEnabled(out composed)==0&&composed)Check("dock opts out of Windows screen capture",DockPresentation.GetWindowDisplayAffinity(dockHandle,out affinity)&&affinity==DockPresentation.ExcludeFromCapture);else Console.WriteLine("SKIP native capture affinity: desktop compositor unavailable");
                var board=(Canvas)Field(dock,"board");var scroll=(ScrollViewer)Field(dock,"scroll");double cell=TaskMenuGrid.CellSize(p);
                var grip=Find<System.Windows.Controls.Primitives.Thumb>(dock,t=>Convert.ToString(t.ToolTip)=="Переместить меню вдоль края");
                Check("panel has a separate edge movement grip",grip!=null);
                if(grip!=null){
                    double before=p.Position;Native.RECT initial;Native.GetWindowRect(dockHandle,out initial);
                    grip.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});
                    Point start=(Point)Field(dock,"panelDragPointer");dock.GetType().GetMethod("MovePanelTo",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dock,new object[]{new Point(start.X+400,start.Y+120)});
                    Native.RECT moved;Native.GetWindowRect(dockHandle,out moved);Check("side panel drag changes only vertical native position",moved.Left==initial.Left&&moved.Top>initial.Top&&p.Position>before);
                    dock.SetExpanded(false,true);Check("panel remains expanded throughout movement",dock.IsExpanded&&changed==0);
                    grip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,120,true){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});
                    Check("cancelled panel drag restores position without saving",p.Position==before&&changed==0);
                    grip.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});start=(Point)Field(dock,"panelDragPointer");dock.GetType().GetMethod("MovePanelTo",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dock,new object[]{new Point(start.X,start.Y+80)});
                    grip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(0,80,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});
                    Check("completed panel drag saves once",changed==1&&p.Position>before);changed=0;
                }
                Check("logical board retains empty columns and rows",board.Width==4*cell&&board.Height==3*cell);
                Check("sparse board creates occupied controls and one highlight",board.Children.Count==3&&board.Background is DrawingBrush);
                Check("saved tile sits at exact cell coordinate",Canvas.GetLeft(board.Children[1])==3*cell+2&&Canvas.GetTop(board.Children[1])==2*cell+2);
                var foreign=new DataObject();foreign.SetData("ReFix.TaskMenu.Tile",new DockWindow.DragPayload("other-panel",p.Items[0]));var rejected=Drag(dock,board,foreign,DragDrop.DragOverEvent,1,1);Check("foreign panel drag rejected",rejected.Effects==DragDropEffects.None);
                var own=new DataObject();own.SetData("ReFix.TaskMenu.Tile",new DockWindow.DragPayload(p.Id,p.Items[0]));var over=Drag(dock,board,own,DragDrop.DragOverEvent,1,1);Check("own drag offers Move and highlights destination",over.Effects==DragDropEffects.Move&&((Border)Field(dock,"dropTarget")).Visibility==Visibility.Visible);
                var nativeFormat=new System.Runtime.InteropServices.ComTypes.FORMATETC{cfFormat=unchecked((short)DataFormats.GetDataFormat("ReFix.TaskMenu.Tile").Id),dwAspect=System.Runtime.InteropServices.ComTypes.DVASPECT.DVASPECT_CONTENT,lindex=-1,tymed=System.Runtime.InteropServices.ComTypes.TYMED.TYMED_HGLOBAL};
                var medium=new System.Runtime.InteropServices.ComTypes.STGMEDIUM();bool marshalOk=false;
                try{((System.Runtime.InteropServices.ComTypes.IDataObject)own).GetData(ref nativeFormat,out medium);marshalOk=medium.unionmember!=IntPtr.Zero;}catch(Exception ex){Console.WriteLine("OLE serialization: "+ex.GetType().Name);}finally{if(medium.unionmember!=IntPtr.Zero)ReleaseStgMedium(ref medium);}
                Check("drag payload supports native OLE transport",marshalOk);
                Set(dock,"dragging",true);var dropped=Drag(dock,board,own,DragDrop.DropEvent,1,1);Check("routed drop changes grid cell",dropped.Effects==DragDropEffects.Move&&p.Cells[p.Items[0]].Column==1&&p.Cells[p.Items[0]].Row==1);
                Check("source drag defers persistence until native drag loop returns",changed==0&&(bool)Field(dock,"pendingChanged"));Set(dock,"dragging",false);
                dock.GetType().GetMethod("EndHover",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dock,null);Check("drop highlight cleared after drag",((Border)Field(dock,"dropTarget")).Visibility==Visibility.Collapsed);
                Check("public move swaps occupied cells",dock.TryMoveTile(p.Items[0],3,2)&&p.Cells[p.Items[1]].Column==1&&p.Cells[p.Items[1]].Row==1);
#if TASK_MENU_NATIVE_INPUT_PROBE
                dock.GetType().GetMethod("RenderItems",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dock,null);Pump(30);
                var sourceButton=(Button)board.Children[0];dock.Activate();sourceButton.Focus();Pump(50);if(GetForegroundWindow()!=new WindowInteropHelper(dock).Handle)throw new InvalidOperationException("Native input probe requires the fixture in the foreground.");sourceButton.PreviewMouseLeftButtonUp+=delegate(object sender,System.Windows.Input.MouseButtonEventArgs e){e.Handled=true;};
                Point from=sourceButton.PointToScreen(new Point(sourceButton.ActualWidth/2,sourceButton.ActualHeight/2));Point to=board.PointToScreen(new Point(2*cell+cell/2,cell/2));var oldCursor=System.Windows.Forms.Cursor.Position;Native.RECT nativeBounds;if(!Native.GetWindowRect(new WindowInteropHelper(dock).Handle,out nativeBounds)||!nativeBounds.ToRect().Contains(from)||!nativeBounds.ToRect().Contains(to))throw new InvalidOperationException("Native drag points must lie inside fixture HWND.");
                var input=new Thread(delegate(){try{Thread.Sleep(100);SetCursorPos((int)from.X,(int)from.Y);mouse_event(2,0,0,0,UIntPtr.Zero);Thread.Sleep(120);SetCursorPos((int)from.X+12,(int)from.Y);Thread.Sleep(200);SetCursorPos((int)to.X,(int)to.Y);Thread.Sleep(300);}finally{mouse_event(4,0,0,0,UIntPtr.Zero);Thread.Sleep(100);SetCursorPos(oldCursor.X,oldCursor.Y);}});input.IsBackground=true;input.Start();Pump(1300);input.Join(1000);
                Check("native click-hold mouse drag moves tile horizontally and vertically",p.Cells[p.Items[0]].Column==2&&p.Cells[p.Items[0]].Row==0);
                Check("native drag suppresses launch and restores drag state",(bool)Field(dock,"suppressLaunch")&&!(bool)Field(dock,"dragging")&&!(bool)Field(dock,"dragHover"));
#endif
                TaskMenuGrid.Configure(p,1,2);dock.GetType().GetMethod("RenderItems",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dock,null);dock.SetExpanded(true,true);Pump(30);Drag(dock,board,own,DragDrop.DragOverEvent,0,0);Check("vertical panel previews extra column during drag",board.Width==2*cell);dock.GetType().GetMethod("EndHover",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dock,null);Check("cancel restores original logical board extent",board.Width==cell&&p.Columns==1);
                TaskMenuGrid.Configure(p,12,512);dock.GetType().GetMethod("RenderItems",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(dock,null);dock.SetExpanded(true,true);Pump(50);
                Check("large board does not produce empty cell controls",board.Children.Count==3&&board.Height==512*cell&&scroll.ExtentHeight>scroll.ViewportHeight);
                TaskMenuGrid.Configure(p,3,1);var move=dock.GetType().GetMethod("Move",BindingFlags.Instance|BindingFlags.NonPublic);move.Invoke(dock,new object[]{p.Items[0],1});move.Invoke(dock,new object[]{p.Items[0],1});Check("repeated context move advances through spatial cells",p.Cells[p.Items[0]].Column==2);
                string sourceShortcut=Path.Combine(root,"Dropped.url");File.WriteAllText(sourceShortcut,"[InternetShortcut]\r\nURL=https://example.com/\r\n");var shortcutData=new DataObject(DataFormats.FileDrop,new[]{sourceShortcut});
                Drag(dock,board,shortcutData,DragDrop.DragOverEvent,0,0);var imported=Drag(dock,board,shortcutData,DragDrop.DropEvent,0,0);string menuShortcut=p.Items[p.Items.Count-1];File.Delete(sourceShortcut);
                Check("dropping shortcut stores an independent copy in isolated data root",imported.Effects==DragDropEffects.Copy&&menuShortcut.StartsWith(Path.Combine(root,"task-menu-shortcuts"),StringComparison.OrdinalIgnoreCase)&&File.Exists(menuShortcut));
                dock.SetExpanded(false,true);Check("collapsing remains available after drag",!dock.IsExpanded);dock.Shutdown();
                var bottomPanel=new TaskMenuPanel{Monitor=monitor.Id,Edge=TaskMenuEdge.Bottom};int bottomSaved=0;
                var bottomDock=new DockWindow(bottomPanel,monitor,new ThemeProfile{Mode="Custom"},controls,delegate{bottomSaved++;},delegate{},true,root);bottomDock.Show();Pump(50);bottomDock.SetExpanded(true,true);
                var bottomGrip=Find<System.Windows.Controls.Primitives.Thumb>(bottomDock,t=>true);Native.RECT bottomStart;Native.GetWindowRect(new WindowInteropHelper(bottomDock).Handle,out bottomStart);
                bottomGrip.RaiseEvent(new System.Windows.Controls.Primitives.DragStartedEventArgs(0,0){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragStartedEvent});var bottomPointer=(Point)Field(bottomDock,"panelDragPointer");bottomDock.GetType().GetMethod("MovePanelTo",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(bottomDock,new object[]{new Point(bottomPointer.X+100,bottomPointer.Y-300)});
                Native.RECT bottomEnd;Native.GetWindowRect(new WindowInteropHelper(bottomDock).Handle,out bottomEnd);bottomGrip.RaiseEvent(new System.Windows.Controls.Primitives.DragCompletedEventArgs(100,-300,false){RoutedEvent=System.Windows.Controls.Primitives.Thumb.DragCompletedEvent});
                Check("bottom panel moves horizontally while remaining at bottom edge",bottomEnd.Left>bottomStart.Left&&bottomEnd.Bottom==bottomStart.Bottom&&bottomSaved==1);
                double savedPosition=bottomPanel.Position;bottomDock.Shutdown();var reopened=new DockWindow(bottomPanel,monitor,new ThemeProfile{Mode="Custom"},controls,delegate{},delegate{},false,root);reopened.Show();Pump(50);reopened.SetExpanded(false,true);Native.RECT sliver;Native.GetWindowRect(new WindowInteropHelper(reopened).Handle,out sliver);
                Check("reopened collapsed panel retains moved horizontal position",Math.Abs(sliver.Left-bottomEnd.Left)<=1&&bottomPanel.Position==savedPosition);reopened.Shutdown();
                LayerChecks(root,controls,source,monitor);
                foreach(TaskMenuEdge edge in Enum.GetValues(typeof(TaskMenuEdge)))if(!monitor.Forbidden.Contains(edge)){p.Edge=edge;break;}
                var config=new TaskMenuConfig();config.Panels.Add(p);int saved=0;
                var settings=new SettingsWindow(config,topology,new ThemeProfile{Mode="Custom"},delegate{saved++;});settings.Show();Pump(80);
                var columnField=(TextBox)Field(settings,"columns");var rowField=(TextBox)Field(settings,"rows");var iconField=(TextBox)Field(settings,"size");var widthField=(TextBox)Field(settings,"width");
                var apply=Find<Button>(settings,delegate(Button b){return String.Equals(b.Content as string,"Применить к панели",StringComparison.Ordinal);});
                Check("settings exposes plain icon-size label 14-28",Find<TextBlock>(settings,delegate(TextBlock t){return t.Text=="Размер значков (14–28)";})!=null&&System.Windows.Automation.AutomationProperties.GetName(iconField)=="Размер значков (14–28)");
                Check("settings Apply button exists",apply!=null);
                columnField.Text="5";rowField.Text="3";iconField.Text="14";apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(20);
                Check("settings Apply preserves five columns three rows and 14 px icons",p.Columns==5&&p.Rows==3&&p.IconSize==14&&saved==1);
                widthField.Text="148";Check("settings width 148 maps to four 14 px icon cells",columnField.Text=="4");apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(20);
                Check("settings Apply saves four columns and grid width 148",p.Columns==4&&p.Rows==3&&TaskMenuGrid.Width(p)==148&&saved==2);
                iconField.Text="28";Check("settings icon size 28 updates four-column width to 204",widthField.Text=="204");apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(20);
                Check("settings Apply accepts 28 px icons",p.IconSize==28&&p.Columns==4&&TaskMenuGrid.Width(p)==204&&saved==3);
                iconField.Text="13";apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("settings rejects icons below 14 px",p.IconSize==28&&saved==3&&((TextBlock)Field(settings,"status")).Text.Contains("14–28"));
                iconField.Text="29";apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Check("settings rejects icons above 28 px",p.IconSize==28&&saved==3);
                iconField.Text="28";apply.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));Pump(30);SavePreview(settings,"task-menu-settings-grid.png");settings.Close();
                string oldRoot=Environment.GetEnvironmentVariable("REFIX_DATA_ROOT");Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",root);
                try
                {
                    new TaskMenuStore(root).Save(config);
                    using(var themes=new ThemeStore(root,"task-menu"))using(var controller=new Controller(app,themes,controls,topology))
                    {
                        Pump(50);var before=(System.Collections.IList)Field(controller,"docks");((DockWindow)before[0]).SetExpanded(true,true);
                        controller.GetType().GetMethod("Save",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(controller,null);Pump(80);
                        var after=(System.Collections.IList)Field(controller,"docks");Check("saving dropped layout does not immediately collapse open menu",after.Count==1&&((DockWindow)after[0]).IsExpanded);
                    }
                }
                finally{Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",oldRoot);}
            }return failures;}finally{app.Shutdown();if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
#endif
