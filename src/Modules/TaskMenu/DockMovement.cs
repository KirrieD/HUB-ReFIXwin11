using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
namespace ReFix.TaskMenu
{
    internal sealed partial class DockWindow
    {
        Thumb panelGrip;bool movingPanel;Point panelDragPointer;Rect panelDragBounds;double panelDragPosition;
        void InstallPanelGrip()
        {
            bool vertical=Horizontal;
            panelGrip=new Thumb{Width=vertical?12:32,Height=vertical?32:12,Cursor=vertical?Cursors.SizeNS:Cursors.SizeWE,ToolTip="Переместить меню вдоль края",Focusable=true};
            panelGrip.HorizontalAlignment=vertical?(panel.Edge==ReFix.Shared.TaskMenuEdge.Left?HorizontalAlignment.Right:HorizontalAlignment.Left):HorizontalAlignment.Center;
            panelGrip.VerticalAlignment=vertical?VerticalAlignment.Center:(panel.Edge==ReFix.Shared.TaskMenuEdge.Top?VerticalAlignment.Bottom:VerticalAlignment.Top);
            var hit=new FrameworkElementFactory(typeof(Grid));hit.SetValue(Panel.BackgroundProperty,Brushes.Transparent);
            var line=new FrameworkElementFactory(typeof(Border));line.SetValue(FrameworkElement.WidthProperty,vertical?3d:24d);line.SetValue(FrameworkElement.HeightProperty,vertical?24d:3d);line.SetValue(FrameworkElement.HorizontalAlignmentProperty,HorizontalAlignment.Center);line.SetValue(FrameworkElement.VerticalAlignmentProperty,VerticalAlignment.Center);line.SetValue(Border.CornerRadiusProperty,new CornerRadius(2));line.SetValue(UIElement.OpacityProperty,.65);line.SetResourceReference(Border.BackgroundProperty,"Hub.Muted");hit.AppendChild(line);panelGrip.Template=new ControlTemplate(typeof(Thumb)){VisualTree=hit};
            panelGrip.RenderTransform=slide;System.Windows.Automation.AutomationProperties.SetName(panelGrip,"Переместить меню вдоль края");viewport.Children.Add(panelGrip);
            panelGrip.DragStarted+=delegate{if(disposed)return;SetExpanded(true,true);leave.Stop();var cursor=System.Windows.Forms.Cursor.Position;panelDragPointer=new Point(cursor.X,cursor.Y);panelDragBounds=full;panelDragPosition=panel.Position;movingPanel=true;};
            panelGrip.DragDelta+=delegate{var cursor=System.Windows.Forms.Cursor.Position;MovePanelTo(new Point(cursor.X,cursor.Y));};
            panelGrip.DragCompleted+=delegate(object sender,DragCompletedEventArgs e){if(!movingPanel)return;movingPanel=false;if(disposed)return;if(e.Canceled)panel.Position=panelDragPosition;UpdateGeometry();SetExpanded(true,true);if(!e.Canceled&&Math.Abs(panel.Position-panelDragPosition)>0.000001)changed();if(!IsMouseOver){leave.Stop();leave.Start();}};
        }
        void MovePanelTo(Point pointer)
        {
            if(!movingPanel||disposed)return;
            double coordinate=Horizontal?panelDragBounds.Top+pointer.Y-panelDragPointer.Y:panelDragBounds.Left+pointer.X-panelDragPointer.X;
            panel.Position=ReFix.Shared.TaskMenuGeometry.PositionAt(monitor.Bounds,monitor.Work,panel.Edge,full,coordinate);
            UpdateGeometry();SetExpanded(true,true);
        }
    }
}
