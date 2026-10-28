using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ReFix.Shared;
namespace ReFix.Hub
{
    internal sealed class MixerLayoutWindow : Window
    {
        sealed class Field { internal PropertyInfo Property;internal Slider Slider;internal TextBox Number; }
        readonly ThemeStore themes;readonly MixerLayoutStore layouts;readonly bool ownsLayouts;
        readonly Border frame,image;readonly List<Field> fields=new List<Field>();
        readonly TextBlock notice=Ui.Text("Изменения сразу применяются к открытому микшеру.",12,"Hub.Muted");
        bool closed,refreshing;
        internal MixerLayoutWindow(ThemeStore themes):this(themes,new MixerLayoutStore(themes.Root),true){}
        internal MixerLayoutWindow(ThemeStore themes,MixerLayoutStore layouts):this(themes,layouts,false){}
        MixerLayoutWindow(ThemeStore themes,MixerLayoutStore layouts,bool ownsLayouts)
        {
            this.themes=themes;this.layouts=layouts;this.ownsLayouts=ownsLayouts;
            Title="HUB ReFIX — размеры микшера";Width=580;Height=740;MinWidth=450;MinHeight=400;
            WindowStartupLocation=WindowStartupLocation.CenterOwner;WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;
            Ui.Styles(this);frame=new Border { Margin=new Thickness(18),BorderThickness=new Thickness(1) };Content=frame;
            Grid layers=new Grid();frame.Child=layers;image=new Border { IsHitTestVisible=false };layers.Children.Add(image);
            StackPanel content=new StackPanel { Margin=new Thickness(24) };
            layers.Children.Add(new ScrollViewer { Content=content,VerticalScrollBarVisibility=ScrollBarVisibility.Auto });
            DockPanel title=new DockPanel { Margin=new Thickness(0,0,0,14),Background=Brushes.Transparent };
            Button close=Ui.Button("✕",Close,false);DockPanel.SetDock(close,Dock.Right);title.Children.Add(close);title.Children.Add(Ui.Text("Размеры микшера",23,"Hub.Foreground"));content.Children.Add(title);
            title.MouseLeftButtonDown+=delegate { try { DragMove(); } catch(InvalidOperationException){} };
            content.Children.Add(notice);
            Add(content,"Ширина окна","WindowWidth",280,900);Add(content,"Высота окна","WindowHeight",260,1100);
            Add(content,"Размер текста","BodyFontSize",10,24);Add(content,"Размер заголовка","TitleFontSize",14,32);
            Add(content,"Ширина ползунка · 0 = авто","SliderWidth",0,800);Add(content,"Высота ползунка","SliderHeight",16,60);
            Add(content,"Толщина дорожки","TrackThickness",1,12);Add(content,"Размер бегунка","ThumbSize",10,40);
            Add(content,"Расстояние между строками","RowSpacing",0,28);Add(content,"Размер значка звука","IconSize",16,40);
            Button reset=Ui.Button("Восстановить размеры по умолчанию",delegate { try { layouts.ResetDefaults();Refresh();notice.Text="Компактные размеры восстановлены."; } catch(Exception ex){notice.Text=ex.Message;} },false);
            reset.Margin=new Thickness(0,20,0,0);content.Children.Add(reset);
            themes.Changed+=Changed;layouts.Changed+=Changed;
            Closed+=delegate { closed=true;themes.Changed-=Changed;layouts.Changed-=Changed;if(ownsLayouts)layouts.Dispose(); };
            SizeChanged+=delegate { Ui.Apply(this,themes.Current,frame,image,true); };Refresh();
        }
        void Add(Panel parent,string caption,string property,double min,double max)
        {
            StackPanel section=new StackPanel { Margin=new Thickness(0,14,0,0) };parent.Children.Add(section);
            section.Children.Add(Ui.Text(caption,12,"Hub.Foreground"));
            Grid row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition());row.ColumnDefinitions.Add(new ColumnDefinition { Width=new GridLength(66) });section.Children.Add(row);
            var field=new Field { Property=typeof(MixerLayoutProfile).GetProperty(property),Slider=new Slider { Minimum=min,Maximum=max,TickFrequency=1,IsSnapToTickEnabled=true,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(0,4,12,0) },Number=new TextBox { Margin=new Thickness(0,4,0,0),Padding=new Thickness(5),VerticalContentAlignment=VerticalAlignment.Center } };
            System.Windows.Automation.AutomationProperties.SetName(field.Slider,caption);System.Windows.Automation.AutomationProperties.SetName(field.Number,caption+" — значение");
            row.Children.Add(field.Slider);Grid.SetColumn(field.Number,1);row.Children.Add(field.Number);fields.Add(field);
            field.Slider.ValueChanged+=delegate { if(!refreshing)Commit(field,field.Slider.Value); };
            Action commitNumber=delegate { if(refreshing||closed)return;double value;if(!Double.TryParse(field.Number.Text,NumberStyles.Float,CultureInfo.CurrentCulture,out value)||Double.IsNaN(value)||Double.IsInfinity(value)){notice.Text="Введите конечное число от "+min+" до "+max+".";Refresh();return;}Commit(field,value); };
            field.Number.LostKeyboardFocus+=delegate { commitNumber(); };
            field.Number.KeyDown+=delegate(object sender,KeyEventArgs e) { if(e.Key==Key.Enter){commitNumber();e.Handled=true;} };
        }
        void Commit(Field field,double value)
        {
            try { var p=layouts.Current;field.Property.SetValue(p,value,null);layouts.Save(p);Refresh();notice.Text="Изменения сохранены. Микшер обновлён."; }
            catch(Exception ex) { notice.Text=ex.Message;Refresh(); }
        }
        void Refresh()
        {
            refreshing=true;try { var p=layouts.Current;foreach(var f in fields){double v=(double)f.Property.GetValue(p,null);f.Slider.Value=v;f.Number.Text=v.ToString("0.##",CultureInfo.CurrentCulture);}Ui.Apply(this,themes.Current,frame,image,true); } finally { refreshing=false; }
        }
        void Changed(object sender,EventArgs e) { if(!closed&&!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate { if(!closed)Refresh(); })); }
    }
}
