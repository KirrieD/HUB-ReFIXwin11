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
    internal sealed class DockWindow : Window
    {
        readonly TaskMenuPanel panel;readonly MonitorInfo monitor;readonly Action changed,settings;readonly ControlsStore controls;readonly Grid viewport=new Grid{ClipToBounds=true};readonly TranslateTransform slide=new TranslateTransform();readonly DispatcherTimer leave;
        readonly Border edge=new Border();readonly ThemedSurface surface;readonly ScrollViewer scroll;Rect full;bool expanded,animating,disposed;int revision;double scale=1;ThemeProfile theme;
        internal DockWindow(TaskMenuPanel panel,MonitorInfo monitor,ThemeProfile theme,ControlsStore controls,Action changed,Action settings)
        {
            this.panel=panel;this.monitor=monitor;this.controls=controls;this.changed=changed;this.settings=settings;this.theme=theme;
            ThemeControls.Install(this);
            Title="Меню задач · "+panel.Edge;WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;Background=Brushes.Transparent;Opacity=0;
            viewport.Background=Brushes.Transparent;scroll=new ScrollViewer{VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,Margin=new Thickness(12),BorderThickness=new Thickness(0),Padding=new Thickness(0)};
            surface=new ThemedSurface(scroll);surface.Frame.Margin=new Thickness(0);surface.RenderTransform=slide;viewport.Children.Add(surface);edge.SetResourceReference(Border.BackgroundProperty,"Hub.Accent");viewport.Children.Add(edge);Content=viewport;
            leave=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(300)};leave.Tick+=delegate{leave.Stop();if(!IsMouseOver&&!AnyMenuOpen())SetExpanded(false);};
            MouseEnter+=delegate{leave.Stop();SetExpanded(true);};MouseLeave+=delegate{leave.Stop();leave.Start();};
            ContextMenu=MakeMenu(null);ContextMenu.Closed+=delegate{if(!IsMouseOver){leave.Stop();leave.Start();}};
            AllowDrop=true;DragEnter+=delegate(object s,DragEventArgs e){if(e.Data.GetDataPresent(DataFormats.FileDrop)){leave.Stop();SetExpanded(true);e.Effects=DragDropEffects.Copy;e.Handled=true;}};
            Drop+=delegate(object s,DragEventArgs e){string[] files=e.Data.GetData(DataFormats.FileDrop) as string[];if(files!=null)Add(files);e.Handled=true;};
            PreviewKeyDown+=delegate(object s,KeyEventArgs e){if(KeyChord.Parse(controls.Current.Get("task-menu.hide")).Matches(e.Key==Key.System?e.SystemKey:e.Key,Keyboard.Modifiers)){SetExpanded(false);e.Handled=true;}};
            SourceInitialized+=delegate{var source=HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);source.AddHook(delegate(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){if(msg==0x02E0)Dispatcher.BeginInvoke(new Action(delegate{if(!disposed){UpdateGeometry();SetExpanded(expanded,true);}}));return IntPtr.Zero;});};
            Loaded+=delegate{UpdateGeometry();SetExpanded(!controls.Current.AutoHideTaskMenu,true);Opacity=1;};ApplyTheme(theme);RenderItems();
        }
        bool AnyMenuOpen(){if(ContextMenu!=null&&ContextMenu.IsOpen)return true;foreach(var item in ((WrapPanel)scroll.Content).Children){var b=item as Button;if(b!=null&&b.ContextMenu!=null&&b.ContextMenu.IsOpen)return true;}return false;}
        internal void ApplyTheme(ThemeProfile value){theme=value;surface.Apply(this,value);UpdateClip();}
        void UpdateClip(){double w=surface.Width,h=surface.Height;if(Double.IsNaN(w)||Double.IsNaN(h)||w<=0||h<=0)return;double cut=Math.Min(12,Math.Min(w,h)/4);var shape=new StreamGeometry();using(var c=shape.Open()){c.BeginFigure(new Point(cut,0),true,true);c.PolyLineTo(new[]{new Point(w-cut,0),new Point(w,cut),new Point(w,h-cut),new Point(w-cut,h),new Point(cut,h),new Point(0,h-cut),new Point(0,cut)},true,false);}shape.Freeze();surface.Clip=shape;}
        void UpdateGeometry(){var source=PresentationSource.FromVisual(this);scale=source==null?monitor.Scale:source.CompositionTarget.TransformToDevice.M11;full=TaskMenuGeometry.Expanded(monitor.Bounds,monitor.Work,panel.Edge,panel.Items.Count,panel.Columns,panel.IconSize,scale);surface.Width=full.Width/scale;surface.Height=full.Height/scale;((WrapPanel)scroll.Content).Width=Math.Max(1,Math.Min(panel.Columns,12))*(panel.IconSize+16);((WrapPanel)scroll.Content).MaxWidth=Math.Max(1,full.Width/scale-24);UpdateClip();}
        double HiddenOffset{get{return (panel.Edge==TaskMenuEdge.Left||panel.Edge==TaskMenuEdge.Top)?-(panel.Edge==TaskMenuEdge.Left?full.Width:full.Height)/scale+4:(panel.Edge==TaskMenuEdge.Right?full.Width:full.Height)/scale-4;}}
        bool Horizontal{get{return panel.Edge==TaskMenuEdge.Left||panel.Edge==TaskMenuEdge.Right;}}
        void Position(Rect rect){Width=rect.Width/scale;Height=rect.Height/scale;Native.SetWindowPos(new WindowInteropHelper(this).Handle,new IntPtr(-1),(int)Math.Round(rect.X),(int)Math.Round(rect.Y),Math.Max(1,(int)Math.Round(rect.Width)),Math.Max(1,(int)Math.Round(rect.Height)),0x10|0x40);}
        void EdgeOnly(bool only){edge.Visibility=only?Visibility.Visible:Visibility.Hidden;surface.Visibility=only?Visibility.Hidden:Visibility.Visible;}
        internal bool IsExpanded{get{return expanded;}}
        internal void SetExpanded(bool show){SetExpanded(show,false);}
        internal void SetExpanded(bool show,bool immediate)
        {
            if(disposed)return;if(!controls.Current.AutoHideTaskMenu)show=true;if(!immediate&&show==expanded&&!animating)return;
            int generation=++revision;double current=Horizontal?slide.X:slide.Y;if(!expanded&&!animating)current=HiddenOffset;slide.BeginAnimation(TranslateTransform.XProperty,null);slide.BeginAnimation(TranslateTransform.YProperty,null);expanded=show;
            Position(full);EdgeOnly(false);double target=show?0:HiddenOffset;var property=Horizontal?TranslateTransform.XProperty:TranslateTransform.YProperty;slide.SetValue(property,current);
            Action complete=delegate{if(disposed||generation!=revision)return;animating=false;slide.SetValue(property,target);if(!expanded){Position(TaskMenuGeometry.Sliver(full,panel.Edge,4*scale));EdgeOnly(true);}};
            if(immediate||!SystemParameters.ClientAreaAnimation||SystemParameters.HighContrast||Math.Abs(target-current)<1){complete();return;}
            animating=true;var animation=new DoubleAnimation(current,target,TimeSpan.FromMilliseconds(Math.Max(40,Math.Min(200,200*Math.Abs(target-current)/Math.Max(1,Math.Abs(HiddenOffset)))))){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};animation.Completed+=delegate{complete();};slide.BeginAnimation(property,animation);
        }
        internal void BehaviorChanged(){if(!controls.Current.AutoHideTaskMenu)SetExpanded(true);else if(!IsMouseOver){leave.Stop();leave.Start();}}
        void RenderItems(){var wrap=new WrapPanel();foreach(string path in panel.Items){var button=new Button{Width=panel.IconSize+12,Height=panel.IconSize+12,Margin=new Thickness(2),Padding=new Thickness(4),ToolTip=path,Background=Brushes.Transparent,BorderThickness=new Thickness(0)};var icon=ShellIcon(path,panel.IconSize);button.Content=icon==null?(object)new TextBlock{Text=Directory.Exists(path)?"▣":"◇",FontSize=panel.IconSize*.7}:new Image{Source=icon,Width=panel.IconSize,Height=panel.IconSize};button.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");string captured=path;button.Click+=delegate{try{if(!File.Exists(captured)&&!Directory.Exists(captured)){MessageBox.Show(this,"Файл или папка больше не существует:\n"+captured,"Меню задач");return;}Process.Start(new ProcessStartInfo(captured){UseShellExecute=true});}catch(Exception ex){if(!(ex is System.ComponentModel.Win32Exception||ex is InvalidOperationException||ex is IOException))throw;MessageBox.Show(this,ex.Message,"Не удалось открыть");}};button.ContextMenu=MakeMenu(path);button.ContextMenu.Closed+=delegate{if(!IsMouseOver){leave.Stop();leave.Start();}};wrap.Children.Add(button);}if(panel.Items.Count==0){var add=new Button{Content="+",FontSize=28,Width=panel.IconSize+12,Height=panel.IconSize+12,ToolTip="Добавить файлы или перетащить сюда"};add.Click+=delegate{AddFiles();};wrap.Children.Add(add);}scroll.Content=wrap;}
        MenuItem Item(string title,Action action){var item=new MenuItem{Header=title};item.Click+=delegate{action();};return item;}
        ContextMenu MakeMenu(string path){var menu=new ContextMenu();menu.Items.Add(Item("Добавить файлы…",AddFiles));menu.Items.Add(Item("Добавить папку…",AddFolder));menu.Items.Add(Item("Настроить панели…",settings));if(path!=null){menu.Items.Add(new Separator());menu.Items.Add(Item("Переместить раньше",delegate{Move(path,-1);}));menu.Items.Add(Item("Переместить позже",delegate{Move(path,1);}));menu.Items.Add(Item("Убрать из меню",delegate{panel.Items.Remove(path);changed();}));}return menu;}
        void Move(string path,int delta){int at=panel.Items.IndexOf(path),to=at+delta;if(at<0||to<0||to>=panel.Items.Count)return;panel.Items.RemoveAt(at);panel.Items.Insert(to,path);changed();}
        void AddFiles(){var dialog=new OpenFileDialog{Multiselect=true,Title="Добавить файлы в меню задач",CheckFileExists=true};if(dialog.ShowDialog(this)==true)Add(dialog.FileNames);}
        void AddFolder(){using(var dialog=new System.Windows.Forms.FolderBrowserDialog{Description="Выберите папку для меню задач"}){if(dialog.ShowDialog()==System.Windows.Forms.DialogResult.OK)Add(new[]{dialog.SelectedPath});}}
        void Add(string[] files){foreach(string file in files){string path=TaskMenuConfig.NormalizePath(file);if(path!=null&&(File.Exists(path)||Directory.Exists(path))&&!panel.Items.Exists(delegate(string p){return String.Equals(p,path,StringComparison.OrdinalIgnoreCase);}))panel.Items.Add(path);}changed();}
        [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct SHFILEINFO{public IntPtr icon;public int index;public uint attributes;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=260)]public string name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=80)]public string type;}
        [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern IntPtr SHGetFileInfo(string path,uint attributes,out SHFILEINFO info,uint size,uint flags);
        [DllImport("user32.dll")]static extern bool DestroyIcon(IntPtr icon);
        static ImageSource ShellIcon(string path,int size){SHFILEINFO info;if(SHGetFileInfo(path,0,out info,(uint)Marshal.SizeOf(typeof(SHFILEINFO)),0x100)==IntPtr.Zero||info.icon==IntPtr.Zero)return null;try{var source=Imaging.CreateBitmapSourceFromHIcon(info.icon,Int32Rect.Empty,System.Windows.Media.Imaging.BitmapSizeOptions.FromWidthAndHeight(size,size));source.Freeze();return source;}finally{DestroyIcon(info.icon);}}
        internal void Shutdown(){disposed=true;revision++;leave.Stop();Close();}
    }
}
