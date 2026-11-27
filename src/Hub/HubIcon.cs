using System.IO;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using ReFix.Shared;

namespace ReFix.Hub
{
    internal sealed class HubIcon : Image
    {
        string appearance;
        readonly int size;

        internal HubIcon(int size)
        {
            this.size=size;
            Width=Height=size;
            VerticalAlignment=System.Windows.VerticalAlignment.Center;
            IsHitTestVisible=false;
        }

        internal void Update(string id,ThemeProfile theme)
        {
            int? icon=theme.WindowsIconFor(id);
            string key=icon.HasValue?id+"|Windows|"+icon:id+"|"+theme.Mode+"|"+theme.IconFamily+"|"+theme.IconFollowTheme+"|"+theme.Accent+"|"+theme.Foreground;
            if(appearance==key)return;
            using(var bitmap=ReFixIcons.CreateBitmap(id,size*2,theme))Source=ToSource(bitmap);
            appearance=key;
        }

        internal static BitmapSource ToSource(System.Drawing.Bitmap bitmap)
        {
            using(var stream=new MemoryStream())
            {
                bitmap.Save(stream,System.Drawing.Imaging.ImageFormat.Png);
                stream.Position=0;
                var source=new BitmapImage();source.BeginInit();source.CacheOption=BitmapCacheOption.OnLoad;
                source.StreamSource=stream;source.EndInit();source.Freeze();
                return source;
            }
        }
    }
}
