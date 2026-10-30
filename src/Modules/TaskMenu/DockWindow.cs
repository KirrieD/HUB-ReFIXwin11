using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Microsoft.Win32;
using ReFix.Shared;
using ReFix.Modules;

namespace ReFix.TaskMenu
{
    internal sealed partial class DockWindow : Window
    {
        readonly TaskMenuPanel panel;readonly MonitorInfo monitor;readonly Action changed,settings;readonly ControlsStore controls;readonly Grid viewport=new Grid{ClipToBounds=true};readonly TranslateTransform slide=new TranslateTransform();readonly DispatcherTimer leave;
        readonly Border edge=new Border();readonly ThemedSurface surface;readonly ScrollViewer scroll;readonly bool initiallyExpanded;Rect full;bool expanded,animating,disposed;int revision;double scale=1;ThemeProfile theme;
        const string TileFormat="ReFix.TaskMenu.Tile";
        [Serializable] internal sealed class DragPayload { internal readonly string PanelId,Path;internal DragPayload(string panelId,string path){PanelId=panelId;Path=path;} }
        readonly Canvas board=new Canvas();readonly Border dropTarget=new Border{BorderThickness=new Thickness(2),IsHitTestVisible=false,Visibility=Visibility.Collapsed};
        bool dragging,dragHover,suppressLaunch,pendingChanged;Point press;Button pressed;string pressedPath;int previewColumns,previewRows;
        readonly TaskMenuShortcuts shortcuts;
        bool layerTopmost=true,edgeHover;
        internal IntPtr NativeHandle{get{return new WindowInteropHelper(this).Handle;}}
        internal Rect MonitorBounds{get{return monitor.Bounds;}}
        internal string PanelId{get{return panel.Id;}}
        internal DockWindow(TaskMenuPanel panel,MonitorInfo monitor,ThemeProfile theme,ControlsStore controls,Action changed,Action settings,bool initiallyExpanded=false,string dataRoot=null)
        {
            this.panel=panel;this.monitor=monitor;this.controls=controls;this.changed=changed;this.settings=settings;this.theme=theme;this.initiallyExpanded=initiallyExpanded;shortcuts=new TaskMenuShortcuts(dataRoot??Paths.Root);
            ThemeControls.Install(this);
            Title="Меню задач · "+panel.Edge;WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;layerTopmost=controls.Current.TaskMenuLayer!=TaskMenuLayer.BelowWindows;Topmost=layerTopmost;Background=Brushes.Transparent;Opacity=0;
            TaskMenuGrid.Ensure(panel);viewport.Background=Brushes.Transparent;scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Auto,Margin=new Thickness(12),BorderThickness=new Thickness(0),Padding=new Thickness(0),Content=board,CanContentScroll=false};
            dropTarget.SetResourceReference(Border.BorderBrushProperty,"Hub.Accent");
            surface=new ThemedSurface(scroll);surface.Frame.Margin=new Thickness(0);surface.RenderTransform=slide;viewport.Children.Add(surface);edge.SetResourceReference(Border.BackgroundProperty,"Hub.Accent");viewport.Children.Add(edge);Content=viewport;
            InstallPanelGrip();
            leave=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};leave.Tick+=delegate{leave.Stop();if(!movingPanel&&!dragging&&!dragHover&&!IsMouseOver&&!AnyMenuOpen())SetExpanded(false);};
            MouseEnter+=delegate{leave.Stop();SetExpanded(true);};MouseLeave+=delegate{leave.Stop();leave.Start();};
            ContextMenu=MakeMenu(null);ContextMenu.Closed+=delegate{if(!IsMouseOver){leave.Stop();leave.Start();}};
            AllowDrop=true;DragEnter+=OnDragOver;DragOver+=OnDragOver;DragLeave+=delegate(object s,DragEventArgs e){if(!IsInside(e.GetPosition(this),ActualWidth,ActualHeight))EndHover();};Drop+=OnDrop;
            PreviewKeyDown+=delegate(object s,KeyEventArgs e){if(movingPanel&&e.Key==Key.Escape){panelGrip.CancelDrag();e.Handled=true;return;}if(KeyChord.Parse(controls.Current.Get("task-menu.hide")).Matches(e.Key==Key.System?e.SystemKey:e.Key,Keyboard.Modifiers)){SetExpanded(false);e.Handled=true;}};
            SourceInitialized+=delegate{var handle=new WindowInteropHelper(this).Handle;DockPresentation.Apply(handle);var source=HwndSource.FromHwnd(handle);source.AddHook(delegate(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){if(msg==0x02E0)Dispatcher.BeginInvoke(new Action(delegate{if(!disposed){UpdateGeometry();SetExpanded(expanded,true);}}));return IntPtr.Zero;});};
            Loaded+=delegate{DockPresentation.Apply(new WindowInteropHelper(this).Handle);UpdateGeometry();SetExpanded(initiallyExpanded||!controls.Current.AutoHideTaskMenu,true);DockStacking.Apply(NativeHandle,layerTopmost);if(initiallyExpanded&&controls.Current.AutoHideTaskMenu)leave.Start();};ApplyTheme(theme);RenderItems();
        }
        bool AnyMenuOpen(){if(ContextMenu!=null&&ContextMenu.IsOpen)return true;foreach(var item in board.Children){var b=item as Button;if(b!=null&&b.ContextMenu!=null&&b.ContextMenu.IsOpen)return true;}return false;}
        internal void ApplyTheme(ThemeProfile value){theme=value;surface.Apply(this,value);UpdateClip();}
        void UpdateClip(){double w=surface.Width,h=surface.Height;if(Double.IsNaN(w)||Double.IsNaN(h)||w<=0||h<=0)return;double cut=Math.Min(12,Math.Min(w,h)/4);var shape=new StreamGeometry();using(var c=shape.Open()){c.BeginFigure(new Point(cut,0),true,true);c.PolyLineTo(new[]{new Point(w-cut,0),new Point(w,cut),new Point(w,h-cut),new Point(w-cut,h),new Point(cut,h),new Point(0,h-cut),new Point(0,cut)},true,false);}shape.Freeze();surface.Clip=shape;}
        void UpdateGeometry(){var source=PresentationSource.FromVisual(this);scale=source==null?monitor.Scale:source.CompositionTarget.TransformToDevice.M11;var extent=new TaskMenuPanel{Edge=panel.Edge,Position=panel.Position,Columns=Math.Max(panel.Columns,previewColumns),Rows=Math.Max(panel.Rows,previewRows),IconSize=panel.IconSize,GridConfigured=true};full=TaskMenuGeometry.Expanded(monitor.Bounds,monitor.Work,extent,scale);surface.Width=full.Width/scale;surface.Height=full.Height/scale;board.Width=extent.Columns*TaskMenuGrid.CellSize(panel);board.Height=extent.Rows*TaskMenuGrid.CellSize(panel);UpdateClip();}
        double HiddenOffset{get{return (panel.Edge==TaskMenuEdge.Left||panel.Edge==TaskMenuEdge.Top)?-(panel.Edge==TaskMenuEdge.Left?full.Width:full.Height)/scale+4:(panel.Edge==TaskMenuEdge.Right?full.Width:full.Height)/scale-4;}}
        bool Horizontal{get{return panel.Edge==TaskMenuEdge.Left||panel.Edge==TaskMenuEdge.Right;}}
        void Position(Rect rect){Width=rect.Width/scale;Height=rect.Height/scale;Native.SetWindowPos(new WindowInteropHelper(this).Handle,layerTopmost?new IntPtr(-1):IntPtr.Zero,(int)Math.Round(rect.X),(int)Math.Round(rect.Y),Math.Max(1,(int)Math.Round(rect.Width)),Math.Max(1,(int)Math.Round(rect.Height)),0x10|0x40|(layerTopmost?0u:4u));}
        void EdgeOnly(bool only){bool visible=controls.Current.ShowTaskMenuEdges;Opacity=only&&!visible?0:1;edge.Visibility=only&&visible?Visibility.Visible:Visibility.Hidden;surface.Visibility=only?Visibility.Hidden:Visibility.Visible;panelGrip.Visibility=only?Visibility.Hidden:Visibility.Visible;}
        internal void ApplyLayer(bool fullscreen){if(disposed)return;layerTopmost=TaskMenuLayerPolicy.Topmost(controls.Current.TaskMenuLayer,fullscreen);if(Topmost!=layerTopmost)Topmost=layerTopmost;DockStacking.Apply(NativeHandle,layerTopmost);}
        internal void ObservePointer(Point pointer){if(disposed)return;bool inside=TaskMenuGeometry.Sliver(full,panel.Edge,4*scale).Contains(pointer);if(inside&&!edgeHover){leave.Stop();SetExpanded(true);if(!IsMouseOver)leave.Start();}edgeHover=inside;}
        internal bool IsExpanded{get{return expanded;}}
        internal void SetExpanded(bool show){SetExpanded(show,false);}
        internal void SetExpanded(bool show,bool immediate)
        {
            if(disposed)return;if(movingPanel||dragging||dragHover)show=true;if(!controls.Current.AutoHideTaskMenu)show=true;if(!immediate&&show==expanded&&!animating)return;
            int generation=++revision;double current=Horizontal?slide.X:slide.Y;if(!expanded&&!animating)current=HiddenOffset;slide.BeginAnimation(TranslateTransform.XProperty,null);slide.BeginAnimation(TranslateTransform.YProperty,null);expanded=show;
            Position(full);EdgeOnly(false);double target=show?0:HiddenOffset;var property=Horizontal?TranslateTransform.XProperty:TranslateTransform.YProperty;slide.SetValue(property,current);
            Action complete=delegate{if(disposed||generation!=revision)return;animating=false;slide.SetValue(property,target);if(!expanded){Position(TaskMenuGeometry.Sliver(full,panel.Edge,4*scale));EdgeOnly(true);}};
            if(immediate||!SystemParameters.ClientAreaAnimation||SystemParameters.HighContrast||Math.Abs(target-current)<1){complete();return;}
            animating=true;var animation=new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(Math.Max(40,Math.Min(200,200*Math.Abs(target-current)/Math.Max(1,Math.Abs(HiddenOffset)))))){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};animation.Completed+=delegate{complete();};slide.BeginAnimation(property,animation);
        }
        internal void BehaviorChanged(){edgeHover=false;EdgeOnly(!expanded&&!animating);if(!controls.Current.AutoHideTaskMenu)SetExpanded(true);else if(!IsMouseOver){leave.Stop();leave.Start();}}
        void RenderItems()
        {
            TaskMenuGrid.Ensure(panel);board.Children.Clear();double cell=TaskMenuGrid.CellSize(panel);
            var lines=new GeometryGroup();lines.Children.Add(new LineGeometry(new Point(cell-.5,0),new Point(cell-.5,cell)));lines.Children.Add(new LineGeometry(new Point(0,cell-.5),new Point(cell,cell-.5)));lines.Freeze();
            var drawing=new GeometryDrawing(null,new Pen(new SolidColorBrush(Color.FromArgb(36,128,128,128)),1),lines);drawing.Freeze();
            var brush=new DrawingBrush(drawing){TileMode=TileMode.Tile,ViewportUnits=BrushMappingMode.Absolute,Viewport=new Rect(0,0,cell,cell),ViewboxUnits=BrushMappingMode.Absolute,Viewbox=new Rect(0,0,cell,cell),Stretch=Stretch.None};brush.Freeze();board.Background=brush;
            foreach(string path in panel.Items)
            {
                var button=new Button{Width=cell-4,Height=cell-4,Padding=new Thickness(4),ToolTip=path,Background=Brushes.Transparent,BorderThickness=new Thickness(0)};
                var icon=ShellIcon(path,panel.IconSize);button.Content=icon==null?(object)new TextBlock{Text=Directory.Exists(path)?"▣":"◇",FontSize=panel.IconSize*.7}:new Image{Source=icon,Width=panel.IconSize,Height=panel.IconSize};
                button.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");string captured=path;
                button.Click+=delegate{if(!suppressLaunch&&!dragging)Launch(captured);};
                button.PreviewMouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){if(dragging)return;suppressLaunch=false;pressed=button;pressedPath=captured;press=e.GetPosition(board);};
                button.PreviewMouseMove+=OnTileMouseMove;
                button.PreviewMouseLeftButtonUp+=delegate{pressed=null;pressedPath=null;};
                button.ContextMenu=MakeMenu(path);button.ContextMenu.Closed+=delegate{if(!IsMouseOver){leave.Stop();leave.Start();}};
                TaskMenuCell at=panel.Cells[path];Canvas.SetLeft(button,at.Column*cell+2);Canvas.SetTop(button,at.Row*cell+2);board.Children.Add(button);
            }
            if(panel.Items.Count==0){var add=new Button{Content="+",FontSize=28,Width=cell-4,Height=cell-4,ToolTip="Добавить файлы или перетащить сюда"};add.Click+=delegate{AddFiles();};Canvas.SetLeft(add,2);Canvas.SetTop(add,2);board.Children.Add(add);}
            board.Children.Add(dropTarget);dropTarget.Width=cell;dropTarget.Height=cell;UpdateGeometry();
        }
        void Launch(string path)
        {
            try{if(!File.Exists(path)&&!Directory.Exists(path)){MessageBox.Show(this,"Файл или папка больше не существует:\n"+path,"Меню задач");return;}Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
            catch(Exception ex){if(!(ex is System.ComponentModel.Win32Exception||ex is InvalidOperationException||ex is IOException))throw;MessageBox.Show(this,ex.Message,"Не удалось открыть");}
        }
        void OnTileMouseMove(object sender,MouseEventArgs e)
        {
            if(dragging||pressed==null||pressed!=sender||e.LeftButton!=MouseButtonState.Pressed)return;
            Point point=e.GetPosition(board);if(Math.Abs(point.X-press.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(point.Y-press.Y)<SystemParameters.MinimumVerticalDragDistance)return;
            var source=pressed;string path=pressedPath;pressed=null;pressedPath=null;suppressLaunch=true;dragging=true;leave.Stop();var focus=Keyboard.FocusedElement;
            try{Mouse.Capture(null);BeginHover();var data=new DataObject();data.SetData(TileFormat,new DragPayload(panel.Id,path));DragDrop.DoDragDrop(source,data,DragDropEffects.Move);}
            finally{dragging=false;EndHover();if(!disposed&&focus!=null)Keyboard.Focus(focus);if(pendingChanged){pendingChanged=false;changed();}e.Handled=true;}
        }
        static bool IsInside(Point p,double width,double height){return p.X>=0&&p.Y>=0&&p.X<width&&p.Y<height;}
        bool Accepts(IDataObject data)
        {
            if(data.GetDataPresent(TileFormat)){var tile=data.GetData(TileFormat) as DragPayload;return tile!=null&&String.Equals(tile.PanelId,panel.Id,StringComparison.Ordinal)&&panel.Items.Contains(tile.Path);}
            return data.GetDataPresent(DataFormats.FileDrop);
        }
        void BeginHover()
        {
            leave.Stop();if(dragHover)return;dragHover=true;
            previewColumns=Math.Min(12,panel.Columns==1?2:panel.Columns);previewRows=Math.Min(512,panel.Rows==1?2:panel.Rows);
            UpdateGeometry();SetExpanded(true,true);
        }
        void EndHover()
        {
            if(dragging){dropTarget.Visibility=Visibility.Collapsed;return;}
            dragHover=false;previewColumns=previewRows=0;dropTarget.Visibility=Visibility.Collapsed;
            if(!disposed){UpdateGeometry();SetExpanded(expanded,true);if(!IsMouseOver){leave.Stop();leave.Start();}}
        }
        bool Target(DragEventArgs e,out int column,out int row)
        {
            // Board coordinates already include the ScrollViewer's physical offsets.
            Point p=e.GetPosition(board);double size=TaskMenuGrid.CellSize(panel);column=(int)Math.Floor(p.X/size);row=(int)Math.Floor(p.Y/size);
            return p.X>=0&&p.Y>=0&&column<Math.Max(panel.Columns,previewColumns)&&row<Math.Max(panel.Rows,previewRows)&&column<12&&row<512;
        }
        void OnDragOver(object sender,DragEventArgs e)
        {
            e.Handled=true;e.Effects=DragDropEffects.None;if(!Accepts(e.Data)){dropTarget.Visibility=Visibility.Collapsed;return;}
            BeginHover();Point p=e.GetPosition(scroll);const double inset=18,step=12;
            if(p.Y<inset)scroll.ScrollToVerticalOffset(Math.Max(0,scroll.VerticalOffset-step));else if(p.Y>scroll.ActualHeight-inset)scroll.ScrollToVerticalOffset(scroll.VerticalOffset+step);
            if(p.X<inset)scroll.ScrollToHorizontalOffset(Math.Max(0,scroll.HorizontalOffset-step));else if(p.X>scroll.ActualWidth-inset)scroll.ScrollToHorizontalOffset(scroll.HorizontalOffset+step);
            int column,row;if(!Target(e,out column,out row)){dropTarget.Visibility=Visibility.Collapsed;return;}
            double cell=TaskMenuGrid.CellSize(panel);Canvas.SetLeft(dropTarget,column*cell);Canvas.SetTop(dropTarget,row*cell);dropTarget.Visibility=Visibility.Visible;e.Effects=e.Data.GetDataPresent(TileFormat)?DragDropEffects.Move:DragDropEffects.Copy;
        }
        void OnDrop(object sender,DragEventArgs e)
        {
            e.Handled=true;e.Effects=DragDropEffects.None;
            if(Accepts(e.Data))
            {
                int column,row;bool target=Target(e,out column,out row);
                if(e.Data.GetDataPresent(TileFormat)){var tile=(DragPayload)e.Data.GetData(TileFormat);if(target&&TryMoveTile(tile.Path,column,row))e.Effects=DragDropEffects.Move;}
                else{var files=e.Data.GetData(DataFormats.FileDrop) as string[];if(files!=null){Add(files);e.Effects=DragDropEffects.Copy;}}
            }
            EndHover();
        }
        internal bool TryMoveTile(string path,int column,int row)
        {
            if(!TaskMenuGrid.Move(panel,path,column,row))return false;
            if(dragging)pendingChanged=true;else changed();return true;
        }
        MenuItem Item(string title,Action action){var item=new MenuItem{Header=title};item.Click+=delegate{action();};return item;}
        ContextMenu MakeMenu(string path){var menu=new ContextMenu();menu.Items.Add(Item("Добавить файлы…",AddFiles));menu.Items.Add(Item("Добавить папку…",AddFolder));menu.Items.Add(Item("Настроить панели…",settings));if(path!=null){menu.Items.Add(new Separator());menu.Items.Add(Item("Переместить раньше",delegate{Move(path,-1);}));menu.Items.Add(Item("Переместить позже",delegate{Move(path,1);}));menu.Items.Add(Item("Убрать из меню",delegate{panel.Items.Remove(path);changed();}));}return menu;}
        void Move(string path,int delta){TaskMenuGrid.Ensure(panel);TaskMenuCell cell;if(!panel.Cells.TryGetValue(path,out cell))return;int to=cell.Row*panel.Columns+cell.Column+delta;if(to<0||to>=panel.Columns*panel.Rows)return;TryMoveTile(path,to%panel.Columns,to/panel.Columns);}
        void AddFiles(){var dialog=new OpenFileDialog{Multiselect=true,DereferenceLinks=false,Title="Добавить файлы в меню задач",CheckFileExists=true};if(dialog.ShowDialog(this)==true)Add(dialog.FileNames);}
        void AddFolder(){using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description="Выберите папку для меню задач"}){if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK)Add(new[]{dialog.SelectedPath});}}
        void Add(string[] files){bool added=false;string error=null;foreach(string file in files){string path=TaskMenuConfig.NormalizePath(file);if(path==null||(!File.Exists(path)&&!Directory.Exists(path))||panel.Items.Count>=512)continue;try{path=shortcuts.Import(path);if(!panel.Items.Exists(delegate(string p){return String.Equals(p,path,StringComparison.OrdinalIgnoreCase);})) {panel.Items.Add(path);added=true;}}catch(Exception ex){if(!(ex is IOException||ex is UnauthorizedAccessException||ex is ArgumentException))throw;error=ex.Message;}}if(added){TaskMenuGrid.Ensure(panel);if(dragging)pendingChanged=true;else changed();}if(error!=null)MessageBox.Show(this,error,"Не удалось добавить ярлык");}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct SHFILEINFO{public IntPtr icon;public int index;public uint attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string type;}
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SHGetFileInfo(string path,uint attributes,out SHFILEINFO info,uint size,uint flags);
        [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
        static ImageSource ShellIcon(string path,int size){SHFILEINFO info;if(SHGetFileInfo(path,0,out info,(uint)Marshal.SizeOf(typeof(SHFILEINFO)),0x100)==IntPtr.Zero||info.icon==IntPtr.Zero)return null;try{var source=Imaging.CreateBitmapSourceFromHIcon(info.icon,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(size,size));source.Freeze();return source;}finally{DestroyIcon(info.icon);}}
        internal void Shutdown(){disposed=true;revision++;leave.Stop();Close();}
    }
}
