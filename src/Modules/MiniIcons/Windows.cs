using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using ReFix.Shared;
using ReFix.Modules;
using ReFix.TaskMenu;
namespace ReFix.MiniIcons
{
    internal static class Visuals
    {
        internal static TextBlock Text(string value,double size=13,string resource="Hub.Foreground"){var text=new TextBlock{Text=value,FontSize=size,TextWrapping=TextWrapping.Wrap};text.SetResourceReference(TextBlock.ForegroundProperty,resource);return text;}
        internal static Image Icon(string id,int size,ThemeProfile theme){using(var bitmap=ReFixIcons.CreateBitmap(id,size*2,theme))using(var stream=new MemoryStream()){bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);stream.Position=0;var image=new BitmapImage();image.BeginInit();image.CacheOption=BitmapCacheOption.OnLoad;image.StreamSource=stream;image.EndInit();image.Freeze();return new Image{Source=image,Width=size,Height=size};}}
        internal static Button Button(string text,Action action){var button=new Button{Content=text,Padding=new Thickness(10,7,10,7),Margin=new Thickness(0,6,0,0)};button.Click+=delegate{action();};return button;}
        internal static MenuItem Menu(string text,Action action){var item=new MenuItem{Header=text};item.Click+=delegate{action();};return item;}
    }
    internal sealed class EdgeWindow:Window
    {
        readonly ThemedSurface surface;readonly Action toggle,settings,exit;readonly Action<IntPtr,bool> request;
        internal IntPtr Handle{get{return new WindowInteropHelper(this).Handle;}}
        internal EdgeWindow(ThemeProfile theme,Action toggle,Action settings,Action exit,Action<IntPtr,bool> request)
        {
            this.toggle=toggle;this.settings=settings;this.exit=exit;this.request=request;
            Title="Мини значки · открыть меню";WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=false;Topmost=true;Width=26;Height=40;Background=Brushes.Transparent;
            ThemeControls.Install(this);var button=new Button{Content="›",FontSize=25,Padding=new Thickness(0),ToolTip="Мини значки · открыть меню"};button.Click+=delegate{toggle();};
            surface=new ThemedSurface(button);surface.Frame.Margin=new Thickness(0);Content=surface;ApplyTheme(theme);
            var menu=new ContextMenu();menu.Items.Add(Visuals.Menu("Открыть меню",toggle));menu.Items.Add(Visuals.Menu("Настройки",settings));menu.Items.Add(new Separator());menu.Items.Add(Visuals.Menu("Выключить мини значки",exit));ContextMenu=menu;
            SourceInitialized+=delegate{DockPresentation.Apply(Handle);HwndSource.FromHwnd(Handle).AddHook(Message);};
        }
        IntPtr Message(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){if(msg==MiniTrayFiles.TransferMessage||msg==MiniTrayFiles.RestoreMessage){request(w,msg==MiniTrayFiles.RestoreMessage);handled=true;}return IntPtr.Zero;}
        internal void ApplyTheme(ThemeProfile value){surface.Apply(this,value);}
        double? alignedY;
        internal void AlignToPopup(MonitorInfo monitor,Rect popup){alignedY=popup.Top+(popup.Height-40*monitor.Scale)/2;Place(monitor);}
        internal void Place(MonitorInfo monitor){Rect r=MiniIconsGeometry.Edge(monitor.Bounds,monitor.Work,monitor.Scale);if(alignedY.HasValue)r.Y=Math.Max(monitor.Work.Top,Math.Min(alignedY.Value,monitor.Work.Bottom-r.Height));Native.SetWindowPos(Handle,new IntPtr(-1),(int)r.X,(int)r.Y,Math.Max(1,(int)r.Width),Math.Max(1,(int)r.Height),0x10);}
    }
    internal sealed class PopupWindow:Window
    {
        const string DragFormat="ReFix.MiniIcons.Own.v1";
        readonly StackPanel body=new StackPanel();readonly UniformGrid icons=new UniformGrid();readonly ThemedSurface surface;readonly Action<string,bool> transfer;readonly Action<IList<string>> reorder;readonly Action settings;readonly Action openExternal;readonly Border separator;readonly Button external;
        MiniIconsConfig config;ThemeProfile theme;List<MiniTrayEntry> entries=new List<MiniTrayEntry>();bool dragging,suppressLaunch;Point pressedAt;MiniTrayEntry pressed;IList<string> pendingOrder;
        readonly TranslateTransform slide=new TranslateTransform();int motionRevision;
        internal bool IsOpening{get;private set;} internal bool IsClosing{get;private set;} TaskMenuEdge motionSide;
        [Serializable]sealed class IconDrag{internal string Id;internal IconDrag(string id){Id=id;}}
        internal IntPtr Handle{get{return new WindowInteropHelper(this).Handle;}}
        internal PopupWindow(ThemeProfile theme,Action<string,bool> transfer,Action<IList<string>> reorder,Action settings,Action openExternal)
        {
            this.transfer=transfer;this.reorder=reorder;this.settings=settings;this.openExternal=openExternal;
            Title="Мини значки";WindowStyle=WindowStyle.None;AllowsTransparency=true;ResizeMode=ResizeMode.NoResize;ShowInTaskbar=false;ShowActivated=true;Topmost=true;Background=Brushes.Transparent;Width=68;SizeToContent=SizeToContent.Height;
            ThemeControls.Install(this);body.Margin=new Thickness(4);body.Children.Add(icons);
            separator=new Border{Height=1,Margin=new Thickness(0,4,0,4)};separator.SetResourceReference(Border.BackgroundProperty,"Hub.Border");body.Children.Add(separator);
            external=IconButton(Visuals.Text("▦",16),24,"Скрытые значки Windows");external.HorizontalAlignment=HorizontalAlignment.Center;external.Click+=delegate{openExternal();};body.Children.Add(external);
            surface=new ThemedSurface(body);surface.Frame.Margin=new Thickness(6);surface.RenderTransform=slide;var viewport=new Grid{ClipToBounds=true};viewport.Children.Add(surface);Content=viewport;ApplyTheme(theme);
            var popupMenu=new ContextMenu();popupMenu.Items.Add(Visuals.Menu("Настройки",settings));ContextMenu=popupMenu;
            Deactivated+=delegate{if(!dragging&&AutoHide&&!CaptureVisibility.IsCaptureActive&&(ContextMenu==null||!ContextMenu.IsOpen)&&!icons.Children.OfType<Button>().Any(b=>b.ContextMenu!=null&&b.ContextMenu.IsOpen))HideAnimated();};PreviewKeyDown+=delegate(object sender,KeyEventArgs e){if(KeyChord.Parse(HideGesture).Matches(e.Key==Key.System?e.SystemKey:e.Key,Keyboard.Modifiers)){HideAnimated();e.Handled=true;}};
            SourceInitialized+=delegate{DockPresentation.Apply(Handle);};
            IsVisibleChanged+=delegate{if(!IsVisible)ResetOpening();};Closed+=delegate{ResetOpening();};
        }
        internal bool AutoHide=true;internal string HideGesture="Esc";
        internal void Refresh(MiniIconsConfig value,IList<MiniTrayEntry> list,ThemeProfile appearance)
        {
            config=value.Clone();theme=appearance;entries=list.OrderBy(e=>{int index=config.OwnOrder.IndexOf(e.Id);return index<0?int.MaxValue:index;}).ThenBy(e=>e.Name).ToList();icons.Children.Clear();icons.Columns=Math.Max(1,Math.Min(config.Columns,entries.Count));
            separator.Visibility=external.Visibility=config.ShowWindowsTray?Visibility.Visible:Visibility.Collapsed;
            foreach(var entry in entries){var item=entry;var button=IconButton(Visuals.Icon(entry.Id,config.IconSize,entry.IconTheme??theme),config.IconSize+8,entry.Name+" · v"+entry.Version);
                button.Click+=delegate{if(!dragging&&!suppressLaunch)Invoke(item,false,button);};var menu=new ContextMenu();menu.Items.Add(Visuals.Menu("Открыть",delegate{Invoke(item,false,button);}));menu.Items.Add(Visuals.Menu("Меню приложения",delegate{Invoke(item,true,button);}));menu.Items.Add(new Separator());menu.Items.Add(Visuals.Menu("Вернуть в системный трей",delegate{transfer(item.Id,true);}));button.ContextMenu=menu;
                button.PreviewMouseRightButtonUp+=delegate(object s,MouseButtonEventArgs e){if((Keyboard.Modifiers&ModifierKeys.Shift)==0){Invoke(item,true,button);e.Handled=true;}};
                button.PreviewMouseLeftButtonDown+=delegate(object s,MouseButtonEventArgs e){suppressLaunch=false;pressed=item;pressedAt=e.GetPosition(this);};button.PreviewMouseMove+=delegate(object s,MouseEventArgs e){if(pressed!=item||dragging||e.LeftButton!=MouseButtonState.Pressed)return;Point p=e.GetPosition(this);if(Math.Abs(p.X-pressedAt.X)<SystemParameters.MinimumHorizontalDragDistance&&Math.Abs(p.Y-pressedAt.Y)<SystemParameters.MinimumVerticalDragDistance)return;pressed=null;dragging=true;suppressLaunch=true;try{DragDrop.DoDragDrop(button,new DataObject(DragFormat,new IconDrag(item.Id)),DragDropEffects.Move);}finally{dragging=false;if(pendingOrder!=null){var order=pendingOrder;pendingOrder=null;reorder(order);}}};button.PreviewMouseLeftButtonUp+=delegate{pressed=null;};
                button.AllowDrop=true;button.DragOver+=delegate(object s,DragEventArgs e){e.Handled=true;var drag=e.Data.GetDataPresent(DragFormat)?e.Data.GetData(DragFormat) as IconDrag:null;e.Effects=drag!=null&&entries.Any(x=>x.Id==drag.Id)?DragDropEffects.Move:DragDropEffects.None;};button.Drop+=delegate(object s,DragEventArgs e){e.Handled=true;var drag=e.Data.GetDataPresent(DragFormat)?e.Data.GetData(DragFormat) as IconDrag:null;if(drag==null||drag.Id==item.Id||!entries.Any(x=>x.Id==drag.Id))return;var order=entries.Select(x=>x.Id).ToList();order.Remove(drag.Id);order.Insert(Math.Max(0,order.IndexOf(item.Id)),drag.Id);if(dragging)pendingOrder=order;else reorder(order);e.Effects=DragDropEffects.Move;};icons.Children.Add(button);
            }
            Width=icons.Columns*(config.IconSize+8)+20;ToolTip=null;ApplyTheme(theme);
        }
        static Button IconButton(object content,int size,string hint)
        {
            var button=new Button{Content=content,Width=size,Height=size,Padding=new Thickness(4),Background=Brushes.Transparent,BorderThickness=new Thickness(0),HorizontalContentAlignment=HorizontalAlignment.Center,VerticalContentAlignment=VerticalAlignment.Center,ToolTip=hint};
            button.MouseEnter+=delegate{button.SetResourceReference(Control.BackgroundProperty,"Hub.Surface");};button.MouseLeave+=delegate{button.Background=Brushes.Transparent;};return button;
        }
        void Invoke(MiniTrayEntry entry,bool right,FrameworkElement source){Point p=source.PointToScreen(new Point(source.ActualWidth/2,source.ActualHeight));if(!MiniTrayFiles.Invoke(entry,right,(int)p.X,(int)p.Y))SetStatus("Приложение уже выключено. Откройте его через HUB.");else if(!right&&AutoHide)HideAnimated();}
        internal void SetStatus(string message){ToolTip=message;}
        internal void ApplyTheme(ThemeProfile value){theme=value;surface.Apply(this,value);}
        internal void AnimateOpen(MonitorInfo monitor)
        {
            ResetOpening();if(!SystemParameters.ClientAreaAnimation)return;
            var side=MiniIconsGeometry.TaskbarSide(monitor.Bounds,monitor.Work);motionSide=side;bool horizontal=side==TaskMenuEdge.Left||side==TaskMenuEdge.Right;double from=side==TaskMenuEdge.Left||side==TaskMenuEdge.Top?-24:24;
            int revision=motionRevision;IsOpening=true;
            var motion=new DoubleAnimation(from,0,TimeSpan.FromMilliseconds(config==null?600:config.SharedAnimation?config.AnimationMilliseconds:config.OpenAnimationMilliseconds)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},FillBehavior=FillBehavior.Stop};
            motion.Completed+=delegate{if(revision==motionRevision)ResetOpening();};
            slide.BeginAnimation(horizontal?TranslateTransform.XProperty:TranslateTransform.YProperty,motion);
            surface.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(0,1,TimeSpan.FromMilliseconds(config==null?600:config.SharedAnimation?config.AnimationMilliseconds:config.OpenAnimationMilliseconds)){FillBehavior=FillBehavior.Stop});
        }
        internal void HideAnimated()
        {
            if(!IsVisible||IsClosing)return;if(!SystemParameters.ClientAreaAnimation){Hide();return;}
            double x=slide.X,y=slide.Y,opacity=surface.Opacity;ResetOpening();IsClosing=true;int revision=motionRevision;bool horizontal=motionSide==TaskMenuEdge.Left||motionSide==TaskMenuEdge.Right;double target=motionSide==TaskMenuEdge.Left||motionSide==TaskMenuEdge.Top?-24:24;int duration=config==null?600:config.SharedAnimation?config.AnimationMilliseconds:config.CloseAnimationMilliseconds;
            var motion=new DoubleAnimation(horizontal?x:y,target,TimeSpan.FromMilliseconds(duration)){EasingFunction=new CubicEase{EasingMode=EasingMode.EaseIn},FillBehavior=FillBehavior.HoldEnd};motion.Completed+=delegate{if(revision==motionRevision)Hide();};slide.BeginAnimation(horizontal?TranslateTransform.XProperty:TranslateTransform.YProperty,motion);surface.BeginAnimation(UIElement.OpacityProperty,new DoubleAnimation(opacity,0,TimeSpan.FromMilliseconds(duration)));
        }
        void ResetOpening(){motionRevision++;IsOpening=false;IsClosing=false;slide.BeginAnimation(TranslateTransform.XProperty,null);slide.BeginAnimation(TranslateTransform.YProperty,null);surface.BeginAnimation(UIElement.OpacityProperty,null);slide.X=slide.Y=0;surface.Opacity=1;}
        internal void Place(MonitorInfo monitor,EdgeWindow edge){Rect anchor=MiniIconsGeometry.Edge(monitor.Bounds,monitor.Work,monitor.Scale);double scale=Math.Max(.5,monitor.Scale);MaxHeight=Math.Max(1,monitor.Work.Height/scale);UpdateLayout();Rect r=MiniIconsGeometry.Popup(monitor.Bounds,monitor.Work,anchor,new Size((ActualWidth>0?ActualWidth:Width)*scale,(IsVisible&&ActualHeight>0?ActualHeight:Math.Ceiling((double)Math.Max(1,entries.Count)/Math.Max(1,icons.Columns))*((config==null?16:config.IconSize)+8)+20+(config!=null&&config.ShowWindowsTray?33:0))*scale),6*scale);Native.SetWindowPos(Handle,new IntPtr(-1),(int)r.X,(int)r.Y,(int)r.Width,(int)r.Height,0x10);edge.AlignToPopup(monitor,r);}
    }
}
