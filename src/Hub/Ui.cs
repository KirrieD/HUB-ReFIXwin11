using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using ReFix.Shared;

namespace ReFix.Hub
{
    internal static class Ui
    {
        internal static TextBlock Text(string text, double size, string color)
        {
            TextBlock value=new TextBlock { Text=text, FontSize=size, TextWrapping=TextWrapping.Wrap };
            value.SetResourceReference(TextBlock.ForegroundProperty,color); return value;
        }
        internal static Button Button(string text, Action action, bool accent)
        {
            Button b=new Button { Content=text, Padding=new Thickness(14,8,14,8), MinHeight=34, Cursor=System.Windows.Input.Cursors.Hand };
            b.SetResourceReference(Control.BackgroundProperty,accent?"Hub.Accent":"Hub.Surface");
            b.SetResourceReference(Control.ForegroundProperty,accent?"Hub.AccentText":"Hub.Foreground");
            b.Click+=delegate { action(); }; return b;
        }
        internal static void Styles(Window window)
        {
            ResourceDictionary dictionary=(ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <Style TargetType='Button'>
    <Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/>
    <Setter Property='Background' Value='{DynamicResource Hub.Surface}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Hub.Border}'/>
    <Setter Property='BorderThickness' Value='1'/><Setter Property='FontSize' Value='12'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Button'>
      <Border x:Name='Frame' CornerRadius='6' BorderBrush='{TemplateBinding BorderBrush}' BorderThickness='{TemplateBinding BorderThickness}' Background='{TemplateBinding Background}' Padding='{TemplateBinding Padding}'>
        <ContentPresenter HorizontalAlignment='Center' VerticalAlignment='Center'/>
      </Border>
      <ControlTemplate.Triggers>
        <Trigger Property='IsMouseOver' Value='True'><Setter TargetName='Frame' Property='Opacity' Value='0.78'/></Trigger>
        <Trigger Property='IsPressed' Value='True'><Setter TargetName='Frame' Property='Opacity' Value='0.6'/></Trigger>
        <Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Frame' Property='BorderBrush' Value='{DynamicResource Hub.Accent}'/><Setter TargetName='Frame' Property='BorderThickness' Value='2'/></Trigger>
        <Trigger Property='IsEnabled' Value='False'><Setter TargetName='Frame' Property='Opacity' Value='0.4'/></Trigger>
      </ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='TextBox'>
    <Setter Property='Background' Value='{DynamicResource Hub.Surface}'/><Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/>
    <Setter Property='BorderBrush' Value='{DynamicResource Hub.Border}'/><Setter Property='CaretBrush' Value='{DynamicResource Hub.Accent}'/>
    <Setter Property='Padding' Value='8,6'/><Setter Property='SelectionBrush' Value='{DynamicResource Hub.Accent}'/>
  </Style>
  <Style TargetType='CheckBox'><Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/><Setter Property='Margin' Value='0,8,0,8'/></Style>
  <Style TargetType='Expander'><Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/><Setter Property='Margin' Value='0,4,0,4'/></Style>
  <Style TargetType='Slider'>
    <Setter Property='Minimum' Value='0'/><Setter Property='Maximum' Value='100'/><Setter Property='Height' Value='28'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='Slider'>
      <Grid Background='Transparent' Margin='2,0'><Track x:Name='PART_Track'>
        <Track.DecreaseRepeatButton><RepeatButton Command='Slider.DecreaseLarge'><RepeatButton.Template><ControlTemplate><Border Height='4' CornerRadius='2' Background='{DynamicResource Hub.Accent}'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Command='Slider.IncreaseLarge'><RepeatButton.Template><ControlTemplate><Border Height='4' CornerRadius='2' Background='{DynamicResource Hub.Border}'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb Width='16' Height='16'><Thumb.Template><ControlTemplate><Grid><Ellipse Fill='{DynamicResource Hub.Surface}' Stroke='{DynamicResource Hub.Muted}' StrokeThickness='1'/><Ellipse Fill='{DynamicResource Hub.Accent}' Margin='4'/></Grid></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      </Track></Grid>
      <ControlTemplate.Triggers><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.45'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter Property='Opacity' Value='0.7'/></Trigger></ControlTemplate.Triggers>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ComboBox'>
    <Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/><Setter Property='Background' Value='{DynamicResource Hub.Surface}'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBox'><Grid>
      <ToggleButton Focusable='False' Foreground='{DynamicResource Hub.Foreground}' IsChecked='{Binding IsDropDownOpen,Mode=TwoWay,RelativeSource={RelativeSource TemplatedParent}}'>
        <ToggleButton.Template><ControlTemplate TargetType='ToggleButton'><Border Background='{DynamicResource Hub.Surface}' BorderBrush='{DynamicResource Hub.Border}' BorderThickness='1' CornerRadius='5' Padding='10,7'><ContentPresenter/></Border></ControlTemplate></ToggleButton.Template>
        <Grid><Grid.ColumnDefinitions><ColumnDefinition/><ColumnDefinition Width='22'/></Grid.ColumnDefinitions><ContentPresenter Content='{TemplateBinding SelectionBoxItem}' VerticalAlignment='Center'/><TextBlock Grid.Column='1' Text='⌄' Foreground='{DynamicResource Hub.Foreground}' HorizontalAlignment='Right'/></Grid>
      </ToggleButton>
      <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsDropDownOpen}' Placement='Bottom' AllowsTransparency='True' Focusable='False'>
        <Border MinWidth='{Binding ActualWidth,RelativeSource={RelativeSource TemplatedParent}}' Background='{DynamicResource Hub.Surface}' BorderBrush='{DynamicResource Hub.Border}' BorderThickness='1' CornerRadius='5' Padding='4'><ScrollViewer CanContentScroll='True'><ItemsPresenter KeyboardNavigation.DirectionalNavigation='Contained'/></ScrollViewer></Border>
      </Popup>
    </Grid></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ComboBoxItem'>
    <Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/><Setter Property='Padding' Value='8,6'/>
    <Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ComboBoxItem'><Border x:Name='b' Padding='{TemplateBinding Padding}' Background='Transparent' CornerRadius='4'><ContentPresenter/></Border><ControlTemplate.Triggers><Trigger Property='IsHighlighted' Value='True'><Setter TargetName='b' Property='Background' Value='{DynamicResource Hub.Background}'/></Trigger></ControlTemplate.Triggers></ControlTemplate></Setter.Value></Setter>
  </Style>
  <Style TargetType='ScrollBar'>
    <Setter Property='Width' Value='9'/><Setter Property='Template'><Setter.Value><ControlTemplate TargetType='ScrollBar'>
      <Grid Background='Transparent'><Track x:Name='PART_Track' IsDirectionReversed='True'>
        <Track.DecreaseRepeatButton><RepeatButton Command='ScrollBar.PageUpCommand'><RepeatButton.Template><ControlTemplate><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.DecreaseRepeatButton>
        <Track.IncreaseRepeatButton><RepeatButton Command='ScrollBar.PageDownCommand'><RepeatButton.Template><ControlTemplate><Border Background='Transparent'/></ControlTemplate></RepeatButton.Template></RepeatButton></Track.IncreaseRepeatButton>
        <Track.Thumb><Thumb><Thumb.Template><ControlTemplate><Border CornerRadius='3' Margin='2,0' Background='{DynamicResource Hub.Muted}'/></ControlTemplate></Thumb.Template></Thumb></Track.Thumb>
      </Track></Grid>
    </ControlTemplate></Setter.Value></Setter>
  </Style>
</ResourceDictionary>");
            window.Resources.MergedDictionaries.Add(dictionary);
        }
        internal static void Apply(Window window, ThemeProfile profile, Border frame, FrameworkElement image)
        {
            ThemeProfile resolved=ThemeProfile.Resolve(profile);
            ThemeApplier.Apply(window,profile);
            if(window.AllowsTransparency)window.Background=Brushes.Transparent;
            Color accent=((SolidColorBrush)window.Resources["Hub.Accent"]).Color;
            window.Resources["Hub.AccentText"]=new SolidColorBrush(.2126*accent.R+.7152*accent.G+.0722*accent.B>150?Colors.Black:Colors.White);
            frame.CornerRadius=new CornerRadius(resolved.CornerRadius);
            frame.Effect=resolved.GlowOpacity<=0?null:new System.Windows.Media.Effects.DropShadowEffect
            {
                Color=((SolidColorBrush)ThemeApplier.ColorBrush(resolved.Glow)).Color,
                BlurRadius=resolved.GlowRadius,Opacity=resolved.GlowOpacity,ShadowDepth=0,RenderingBias=System.Windows.Media.Effects.RenderingBias.Performance
            };
            if(image!=null) image.Effect=resolved.ImageBlur>0?new System.Windows.Media.Effects.BlurEffect { Radius=resolved.ImageBlur,RenderingBias=System.Windows.Media.Effects.RenderingBias.Performance }:null;
        }
    }

    internal sealed class ModulePreview : Border
    {
        private readonly Border picture;
        internal ModulePreview(ModuleManifest manifest)
        {
            Width=216; Height=128; CornerRadius=new CornerRadius(8); BorderThickness=new Thickness(1);
            SetResourceReference(BackgroundProperty,"Hub.Background"); SetResourceReference(BorderBrushProperty,"Hub.Border");
            Grid grid=new Grid(); Child=grid;
            picture=new Border(); picture.SetResourceReference(BackgroundProperty,"Hub.Image"); grid.Children.Add(picture);
            StackPanel content=new StackPanel { Margin=new Thickness(12,9,12,9) }; grid.Children.Add(content);
            DockPanel title=new DockPanel();
            TextBlock dots=Ui.Text("●  ●  ●",8,"Hub.Muted"); dots.HorizontalAlignment=HorizontalAlignment.Right; DockPanel.SetDock(dots,Dock.Right); title.Children.Add(dots);
            title.Children.Add(Ui.Text(manifest.Name,10,"Hub.Foreground")); content.Children.Add(title);
            if(manifest.Preview=="mixer")
            {
                content.Children.Add(Ui.Text("Общая громкость",10,"Hub.Muted")); content.Children.Add(Track(.72));
                content.Children.Add(Ui.Text("Приложение",10,"Hub.Muted")); content.Children.Add(Track(.42));
            }
            else if(manifest.Preview=="notes")
            {
                TextBlock note=Ui.Text("Идея для следующего\nрасширения…",12,"Hub.Foreground"); note.Margin=new Thickness(0,14,0,0); content.Children.Add(note);
                Border line=new Border { Height=2,Width=24,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(0,12,0,0) }; line.SetResourceReference(BackgroundProperty,"Hub.Accent"); content.Children.Add(line);
            }
            else
            {
                TextBlock label=Ui.Text("Параметры Windows",12,"Hub.Foreground"); label.Margin=new Thickness(0,18,0,9); content.Children.Add(label);
                content.Children.Add(Track(.65));
            }
            IsHitTestVisible=false;
        }
        internal void ApplyTheme(ThemeProfile profile)
        {
            ThemeProfile p=ThemeProfile.Resolve(profile);CornerRadius=new CornerRadius(p.CornerRadius*.5);
            Effect=p.GlowOpacity<=0?null:new System.Windows.Media.Effects.DropShadowEffect { Color=ThemeApplier.ColorBrush(p.Glow).Color,BlurRadius=p.GlowRadius*.5,Opacity=p.GlowOpacity,ShadowDepth=0 };
            picture.Effect=p.ImageBlur<=0?null:new System.Windows.Media.Effects.BlurEffect { Radius=p.ImageBlur*.5,RenderingBias=System.Windows.Media.Effects.RenderingBias.Performance };
            Clip=new RectangleGeometry(new Rect(0,0,Width,Height),CornerRadius.TopLeft,CornerRadius.TopLeft);
        }
        private static Grid Track(double value)
        {
            Grid g=new Grid { Height=16,Margin=new Thickness(0,2,0,4) };
            Border line=new Border { Height=3,VerticalAlignment=VerticalAlignment.Center,CornerRadius=new CornerRadius(2) }; line.SetResourceReference(BackgroundProperty,"Hub.Border"); g.Children.Add(line);
            Border fill=new Border { Height=3,Width=188*value,HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,CornerRadius=new CornerRadius(2) }; fill.SetResourceReference(BackgroundProperty,"Hub.Accent"); g.Children.Add(fill);
            System.Windows.Shapes.Ellipse thumb=new System.Windows.Shapes.Ellipse { Width=10,Height=10,HorizontalAlignment=HorizontalAlignment.Left,Margin=new Thickness(188*value-5,0,0,0) }; thumb.SetResourceReference(System.Windows.Shapes.Shape.FillProperty,"Hub.Accent"); g.Children.Add(thumb);
            return g;
        }
    }
}
