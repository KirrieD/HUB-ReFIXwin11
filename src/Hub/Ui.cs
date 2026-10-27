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
        internal static TextBox FolderPath(string path)
        {
            return new TextBox { Text=path,IsReadOnly=true,FontSize=11,Padding=new Thickness(6,4,6,4),Margin=new Thickness(0,6,0,0),TextWrapping=TextWrapping.Wrap,MaxHeight=76,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,ToolTip=path };
        }
        internal static void OpenFolder(string path)
        {
            if(string.IsNullOrWhiteSpace(path)||!System.IO.Directory.Exists(path))throw new System.IO.DirectoryNotFoundException("Папка не найдена. Обновите состояние утилиты.");
            using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(System.IO.Path.GetFullPath(path)){UseShellExecute=true,Verb="open"})){}
        }
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
            ThemeControls.Install(window);
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

    internal sealed class ModulePreview : Border,IDisposable
    {
        private readonly Border picture;
        private readonly ThemeStore previewThemes;
        private readonly Window themeCanvas=new Window();
        private bool disposed;
        internal ModulePreview(ModuleManifest manifest,string themeRoot=null)
        {
            previewThemes=new ThemeStore(themeRoot??Paths.Root,manifest.Id=="quiet-mixer"||manifest.Id=="task-menu"?manifest.Id:null);
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
            else if(manifest.Preview=="menu")
            {
                WrapPanel icons=new WrapPanel { Margin=new Thickness(3,17,3,0),HorizontalAlignment=HorizontalAlignment.Center };
                foreach(string symbol in new[]{"▣","▰","◈","▤"})
                {
                    Border tile=new Border { Width=32,Height=34,Margin=new Thickness(4,0,4,0),CornerRadius=new CornerRadius(6) };tile.SetResourceReference(BackgroundProperty,"Hub.Surface");
                    TextBlock icon=Ui.Text(symbol,21,"Hub.Accent");icon.HorizontalAlignment=HorizontalAlignment.Center;icon.VerticalAlignment=VerticalAlignment.Center;tile.Child=icon;icons.Children.Add(tile);
                }
                content.Children.Add(icons);
            }
            else
            {
                TextBlock label=Ui.Text("Параметры Windows",12,"Hub.Foreground"); label.Margin=new Thickness(0,18,0,9); content.Children.Add(label);
                content.Children.Add(Track(.65));
            }
            IsHitTestVisible=false;
            previewThemes.Changed+=PreviewChanged;ApplyTheme(previewThemes.Current);
        }
        private void PreviewChanged(object sender,EventArgs args){if(!disposed&&!Dispatcher.HasShutdownStarted)Dispatcher.BeginInvoke(new Action(delegate{if(!disposed)ApplyTheme(previewThemes.Current);}));}
        internal void ApplyTheme(ThemeProfile profile)
        {
            if(disposed)return;
            profile=previewThemes.Current;
            themeCanvas.Width=Width;themeCanvas.Height=Height;ThemeApplier.Apply(themeCanvas,profile);
            foreach(System.Collections.DictionaryEntry entry in themeCanvas.Resources)Resources[entry.Key]=entry.Value;
            ThemeProfile p=ThemeProfile.Resolve(profile);CornerRadius=new CornerRadius(p.CornerRadius*.5);
            Effect=p.GlowOpacity<=0?null:new System.Windows.Media.Effects.DropShadowEffect { Color=ThemeApplier.ColorBrush(p.Glow).Color,BlurRadius=p.GlowRadius*.5,Opacity=p.GlowOpacity,ShadowDepth=0 };
            picture.Effect=p.ImageBlur<=0?null:new System.Windows.Media.Effects.BlurEffect { Radius=p.ImageBlur*.5,RenderingBias=System.Windows.Media.Effects.RenderingBias.Performance };
            Clip=new RectangleGeometry(new Rect(0,0,Width,Height),CornerRadius.TopLeft,CornerRadius.TopLeft);
        }
        public void Dispose(){if(disposed)return;disposed=true;previewThemes.Changed-=PreviewChanged;previewThemes.Dispose();themeCanvas.Close();}
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
