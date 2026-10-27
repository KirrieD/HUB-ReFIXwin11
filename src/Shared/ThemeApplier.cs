using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
namespace ReFix.Shared
{
    public static class ThemeApplier
    {
        sealed class CachedImage { public string Key; public BitmapSource Image; }
        static readonly object cacheGate=new object();
        static readonly List<CachedImage> cache=new List<CachedImage>();
        public static SolidColorBrush ColorBrush(string hex) {var brush=new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));brush.Freeze();return brush;}
        static BitmapSource LoadImage(string path) {
            var info=new FileInfo(Path.GetFullPath(path));if(!info.Exists||info.Length==0||info.Length>20*1024*1024)return null;
            string key=info.FullName+"|"+info.LastWriteTimeUtc.Ticks+"|"+info.Length;
            lock(cacheGate) {
                for(int i=0;i<cache.Count;i++)if(cache[i].Key==key){CachedImage found=cache[i];cache.RemoveAt(i);cache.Add(found);return found.Image;}
                BitmapSource image;
                using(var input=new FileStream(info.FullName,FileMode.Open,FileAccess.Read,FileShare.Read)) {
                    var decoder=BitmapDecoder.Create(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.None);
                    if(decoder.Frames.Count!=1)return null;var frame=decoder.Frames[0];
                    if(frame.PixelWidth>8192||frame.PixelHeight>8192||(long)frame.PixelWidth*frame.PixelHeight>32000000)return null;
                    input.Position=0;var bitmap=new BitmapImage();bitmap.BeginInit();bitmap.CacheOption=BitmapCacheOption.OnLoad;
                    if(frame.PixelWidth>=frame.PixelHeight)bitmap.DecodePixelWidth=Math.Min(1920,frame.PixelWidth);else bitmap.DecodePixelHeight=Math.Min(1920,frame.PixelHeight);
                    bitmap.StreamSource=input;bitmap.EndInit();bitmap.Freeze();image=bitmap;
                }
                cache.Add(new CachedImage{Key=key,Image=image});if(cache.Count>2)cache.RemoveAt(0);return image;
            }
        }
        static double Dimension(double actual,double requested,double fallback){return actual>0?actual:(!Double.IsNaN(requested)&&!Double.IsInfinity(requested)&&requested>0?requested:fallback);}
        public static void Apply(Window window,ThemeProfile profile) {
            if(window==null)throw new ArgumentNullException("window");ThemeProfile p=ThemeProfile.Resolve(profile);
            var bg=ColorBrush(p.Background).Clone();bg.Opacity=p.BackgroundOpacity;bg.Freeze();window.Resources["Hub.Background"]=bg;
            window.Resources["Hub.Surface"]=ColorBrush(p.Surface);window.Resources["Hub.Foreground"]=ColorBrush(p.Foreground);window.Resources["Hub.Muted"]=ColorBrush(p.Muted);window.Resources["Hub.Accent"]=ColorBrush(p.Accent);window.Resources["Hub.Border"]=ColorBrush(p.Border);window.Resources["Hub.Glow"]=ColorBrush(p.Glow);
            Brush image=Brushes.Transparent;
            try { if(!String.IsNullOrEmpty(p.ImagePath)) {
                BitmapSource bitmap=LoadImage(p.ImagePath);if(bitmap!=null){
                    var brush=new ImageBrush(bitmap);brush.Stretch=Stretch.Fill;brush.Opacity=p.ImageOpacity;
                    double targetAspect=Dimension(window.ActualWidth,window.Width,800)/Dimension(window.ActualHeight,window.Height,600), sourceAspect=(double)bitmap.PixelWidth/bitmap.PixelHeight;
                    if(p.ImageFit=="UniformToFill") {
                        double width=Math.Min(1,targetAspect/sourceAspect),height=Math.Min(1,sourceAspect/targetAspect);
                        brush.ViewboxUnits=BrushMappingMode.RelativeToBoundingBox;brush.Viewbox=new Rect((1-width)*p.ImageX,(1-height)*p.ImageY,width,height);
                    } else if(p.ImageFit=="Uniform") {
                        double width=Math.Min(1,sourceAspect/targetAspect),height=Math.Min(1,targetAspect/sourceAspect);
                        brush.ViewportUnits=BrushMappingMode.RelativeToBoundingBox;brush.Viewport=new Rect((1-width)*p.ImageX,(1-height)*p.ImageY,width,height);brush.TileMode=TileMode.None;
                    }
                    brush.Freeze();image=brush;
                }
            }}catch(IOException){}catch(NotSupportedException){}catch(ArgumentException){}catch(UnauthorizedAccessException){}
            window.Resources["Hub.Image"]=image;window.Resources["Hub.ImageBlur"]=p.ImageBlur;window.Resources["Hub.Corner"]=new CornerRadius(p.CornerRadius);window.Resources["Hub.FontSize"]=p.FontSize;
            window.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");window.SetResourceReference(Control.BackgroundProperty,"Hub.Background");window.SetResourceReference(Control.FontSizeProperty,"Hub.FontSize");window.FontFamily=p.Mode=="System"?SystemFonts.MessageFontFamily:new FontFamily("Segoe UI");
            window.FontStyle=p.Mode=="System"?SystemFonts.MessageFontStyle:FontStyles.Normal;
            window.FontWeight=p.Mode=="System"?SystemFonts.MessageFontWeight:FontWeights.Normal;
        }
    }
}
