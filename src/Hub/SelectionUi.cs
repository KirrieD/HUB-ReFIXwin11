using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
namespace ReFix.Hub
{
    internal static class SelectionUi
    {
        internal static CheckBox Create(string name)
        {
            var box=new CheckBox{VerticalContentAlignment=VerticalAlignment.Center,IsThreeState=false};
            box.Template=(ControlTemplate)XamlReader.Parse(@"<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='CheckBox'>
<StackPanel Orientation='Horizontal' Background='Transparent'><Border x:Name='Box' Width='18' Height='18' CornerRadius='3' BorderThickness='1' BorderBrush='{DynamicResource Hub.Border}' Background='{DynamicResource Hub.Background}'><TextBlock x:Name='Mark' Text='✓' Visibility='Collapsed' FontSize='14' FontWeight='Bold' Foreground='{DynamicResource Hub.AccentText}' HorizontalAlignment='Center' VerticalAlignment='Center'/></Border><ContentPresenter Margin='8,0,0,0' VerticalAlignment='Center'/></StackPanel>
<ControlTemplate.Triggers><Trigger Property='IsChecked' Value='True'><Setter TargetName='Box' Property='Background' Value='{DynamicResource Hub.Accent}'/><Setter TargetName='Mark' Property='Visibility' Value='Visible'/></Trigger><Trigger Property='IsChecked' Value='{x:Null}'><Setter TargetName='Box' Property='Background' Value='{DynamicResource Hub.Accent}'/></Trigger><Trigger Property='IsKeyboardFocused' Value='True'><Setter TargetName='Box' Property='BorderThickness' Value='2'/><Setter TargetName='Box' Property='BorderBrush' Value='{DynamicResource Hub.Tertiary}'/></Trigger><Trigger Property='IsEnabled' Value='False'><Setter Property='Opacity' Value='0.4'/></Trigger></ControlTemplate.Triggers></ControlTemplate>");
            box.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");System.Windows.Automation.AutomationProperties.SetName(box,name);return box;
        }
    }
}
