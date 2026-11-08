using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace ReFix.Shared
{
    public static class ThemeMenu
    {
        public static ContextMenu Create(ResourceDictionary owner)
        {
            var menu = new ContextMenu();
            menu.Resources.MergedDictionaries.Add(owner);
            Apply(menu);
            return menu;
        }

        public static void Apply(ItemsControl menu) { menu.Resources.MergedDictionaries.Add(Styles.Value); }

        static readonly System.Lazy<ResourceDictionary> Styles = new System.Lazy<ResourceDictionary>(() => (ResourceDictionary)XamlReader.Parse(@"
<ResourceDictionary xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
                    xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
    <Style TargetType='ContextMenu'>
        <Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/>
        <Setter Property='Template'><Setter.Value>
            <ControlTemplate TargetType='ContextMenu'>
                <Border Background='{DynamicResource Hub.Surface}' CornerRadius='6' Padding='4'>
                    <ScrollViewer MaxHeight='450' VerticalScrollBarVisibility='Auto'>
                        <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
                    </ScrollViewer>
                </Border>
            </ControlTemplate>
        </Setter.Value></Setter>
    </Style>
    <Style TargetType='Separator'>
        <Setter Property='Height' Value='6'/>
        <Setter Property='Template'>
            <Setter.Value><ControlTemplate TargetType='Separator'><Border/></ControlTemplate></Setter.Value>
        </Setter>
    </Style>
    <Style TargetType='MenuItem'>
        <Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/>
        <Setter Property='Padding' Value='8,4'/>
        <Setter Property='Template'>
            <Setter.Value>
                <ControlTemplate TargetType='MenuItem'>
                    <Border x:Name='row' Background='Transparent' CornerRadius='4' Padding='{TemplateBinding Padding}'>
                        <Grid>
                            <Grid.ColumnDefinitions>
                                <ColumnDefinition Width='Auto'/><ColumnDefinition Width='*'/><ColumnDefinition Width='Auto'/>
                            </Grid.ColumnDefinitions>
                            <TextBlock x:Name='check' Text='✓' Visibility='Collapsed' Width='18'/>
                            <ContentPresenter Grid.Column='1' ContentSource='Header' RecognizesAccessKey='True' VerticalAlignment='Center'/>
                            <TextBlock Grid.Column='2' Text='{TemplateBinding InputGestureText}' Foreground='{DynamicResource Hub.Muted}' Margin='16,0,0,0' VerticalAlignment='Center'/>
                            <TextBlock x:Name='arrow' Grid.Column='2' Text='›' Visibility='Collapsed' HorizontalAlignment='Right' VerticalAlignment='Center'/>
                            <Popup x:Name='PART_Popup' IsOpen='{TemplateBinding IsSubmenuOpen}' Placement='Bottom'
                                   PlacementTarget='{Binding RelativeSource={RelativeSource TemplatedParent}}'
                                   AllowsTransparency='True' Focusable='False'>
                                <Border Background='{DynamicResource Hub.Surface}' CornerRadius='6' Padding='4'>
                                    <ScrollViewer MaxHeight='450' VerticalScrollBarVisibility='Auto'>
                                        <ItemsPresenter KeyboardNavigation.DirectionalNavigation='Cycle'/>
                                    </ScrollViewer>
                                </Border>
                            </Popup>
                        </Grid>
                    </Border>
                    <ControlTemplate.Triggers>
                        <Trigger Property='Role' Value='SubmenuHeader'>
                            <Setter TargetName='PART_Popup' Property='Placement' Value='Right'/>
                            <Setter TargetName='arrow' Property='Visibility' Value='Visible'/>
                        </Trigger>
                        <Trigger Property='IsHighlighted' Value='True'>
                            <Setter TargetName='row' Property='Background' Value='{DynamicResource Hub.Background}'/>
                        </Trigger>
                        <Trigger Property='IsSubmenuOpen' Value='True'>
                            <Setter TargetName='row' Property='Background' Value='{DynamicResource Hub.Background}'/>
                        </Trigger>
                        <Trigger Property='IsChecked' Value='True'><Setter TargetName='check' Property='Visibility' Value='Visible'/></Trigger>
                        <Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='.5'/></Trigger>
                    </ControlTemplate.Triggers>
                </ControlTemplate>
            </Setter.Value>
        </Setter>
    </Style>
</ResourceDictionary>"));
    }
}
