using System.Windows;
using System.Windows.Markup;
namespace ReFix.Shared
{
    public static class ThemeControls
    {
        public static void Install(Window window)
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
            foreach(var type in new[]{typeof(System.Windows.Controls.ListBox),typeof(System.Windows.Controls.ListBoxItem),typeof(System.Windows.Controls.ContextMenu),typeof(System.Windows.Controls.MenuItem)})
            {
                var style=new Style(type);
                style.Setters.Add(new Setter(System.Windows.Controls.Control.ForegroundProperty,new DynamicResourceExtension("Hub.Foreground")));
                style.Setters.Add(new Setter(System.Windows.Controls.Control.BackgroundProperty,new DynamicResourceExtension("Hub.Surface")));
                style.Setters.Add(new Setter(System.Windows.Controls.Control.BorderBrushProperty,new DynamicResourceExtension("Hub.Border")));
                dictionary[type]=style;
            }
            window.Resources.MergedDictionaries.Add(dictionary);
        }
    }
}
