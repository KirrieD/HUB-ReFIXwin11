using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ReFix.Shared;

namespace ReFix.Modules
{
    internal sealed class ThemedSurface : Grid
    {
        internal readonly Border Frame=new Border();
        internal readonly Border BackgroundLayer=new Border();
        internal readonly Border ImageLayer=new Border();
        internal ThemedSurface(UIElement content)
        {
            Frame.Margin=new Thickness(14);Frame.BorderThickness=new Thickness(1);Frame.SetResourceReference(Border.BorderBrushProperty,"Hub.Border");
            Grid layers=new Grid();
            BackgroundLayer.SetResourceReference(Border.BackgroundProperty,"Hub.Background");
            ImageLayer.SetResourceReference(Border.BackgroundProperty,"Hub.Image");
            layers.Children.Add(BackgroundLayer); layers.Children.Add(ImageLayer); layers.Children.Add(content);
            Frame.Child=layers; Children.Add(Frame);
        }
        internal void Apply(Window window,ThemeProfile value)
        {
            ThemeProfile profile=ThemeProfile.Resolve(value);
            ThemeApplier.Apply(window,profile);
            window.Background=Brushes.Transparent;
            window.Opacity=1;
            CornerRadius radius=new CornerRadius(profile.CornerRadius);
            Frame.CornerRadius=radius; BackgroundLayer.CornerRadius=radius; ImageLayer.CornerRadius=radius;
            ImageLayer.Effect=profile.ImageBlur>0 ? new BlurEffect { Radius=profile.ImageBlur,RenderingBias=RenderingBias.Performance } : null;
            Frame.Effect=profile.GlowOpacity>0 ? new DropShadowEffect {
                Color=ThemeApplier.ColorBrush(profile.Glow).Color,Opacity=profile.GlowOpacity,
                BlurRadius=profile.GlowRadius,ShadowDepth=0,RenderingBias=RenderingBias.Performance
            } : null;
        }
    }
}
