using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using ReFix.Shared;

namespace ReFix.QuickNote
{
    internal static class NoteDisclosureStyle
    {
        internal static void Install(Window window)
        {
            window.Resources[typeof(Expander)] = XamlReader.Parse(@"
<Style xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Expander'>
    <Setter Property='Foreground' Value='{DynamicResource Hub.Foreground}'/>
    <Setter Property='Template'>
        <Setter.Value>
            <ControlTemplate TargetType='Expander'>
                <StackPanel>
                    <ToggleButton x:Name='HeaderSite' Content='{TemplateBinding Header}' Foreground='{TemplateBinding Foreground}'
                                  IsChecked='{Binding IsExpanded, RelativeSource={RelativeSource TemplatedParent}, Mode=TwoWay}'>
                        <ToggleButton.Template>
                            <ControlTemplate TargetType='ToggleButton'>
                                <Border Background='Transparent' Padding='3,2'>
                                    <StackPanel Orientation='Horizontal'>
                                        <Path x:Name='glyph' Data='M 1,2 L 5,6 L 9,2' Stroke='{DynamicResource Note.ToggleGlyph}'
                                              StrokeThickness='1.5' Margin='1,0,7,0' VerticalAlignment='Center'/>
                                        <ContentPresenter ContentSource='Content' RecognizesAccessKey='True' VerticalAlignment='Center'/>
                                    </StackPanel>
                                </Border>
                                <ControlTemplate.Triggers>
                                    <Trigger Property='IsChecked' Value='True'>
                                        <Setter TargetName='glyph' Property='Data' Value='M 1,6 L 5,2 L 9,6'/>
                                    </Trigger>
                                </ControlTemplate.Triggers>
                            </ControlTemplate>
                        </ToggleButton.Template>
                    </ToggleButton>
                    <ContentPresenter x:Name='body' ContentSource='Content' Visibility='Collapsed'/>
                </StackPanel>
                <ControlTemplate.Triggers>
                    <Trigger Property='IsExpanded' Value='True'>
                        <Setter TargetName='body' Property='Visibility' Value='Visible'/>
                    </Trigger>
                </ControlTemplate.Triggers>
            </ControlTemplate>
        </Setter.Value>
    </Setter>
</Style>");
        }

        internal static void Apply(Window window, ThemeProfile theme)
        {
            var resolved = ThemeProfile.Resolve(theme);
            window.Resources["Note.ToggleGlyph"] = Contrast(resolved.Background);
            window.Resources["Note.ComboGlyph"] = Contrast(resolved.Surface);
        }

        static Brush Contrast(string background)
        {
            var color = ThemeApplier.ColorBrush(background).Color;
            double luminance = .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
            return luminance > .179 ? Brushes.Black : Brushes.White;
        }

        static double Linear(byte channel)
        {
            double value = channel / 255.0;
            return value <= .04045 ? value / 12.92 : System.Math.Pow((value + .055) / 1.055, 2.4);
        }
    }
}
