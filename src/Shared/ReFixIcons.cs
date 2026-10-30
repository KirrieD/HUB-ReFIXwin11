using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ReFix.Shared
{
    // Callers own each returned bitmap/icon. Rendering never resolves Windows/WPF theme state.
    public static class ReFixIcons
    {
        [DllImport("user32.dll", SetLastError=true)]
        static extern bool DestroyIcon(IntPtr icon);

        public static Icon CreateIcon(string id,int size,ThemeProfile profile)
        {
            using(Bitmap bitmap=CreateBitmap(id,size,profile)) {
                IntPtr handle=bitmap.GetHicon();
                try { using(Icon borrowed=Icon.FromHandle(handle))return (Icon)borrowed.Clone(); }
                finally { DestroyIcon(handle); }
            }
        }
        public static Bitmap CreateBitmap(string id,int size,ThemeProfile profile)
        {
            size=Math.Max(16,Math.Min(256,size));
            ThemeProfile p=(profile??new ThemeProfile()).Clone();p.Normalize();
            string key=(id??"").Trim().ToLowerInvariant();
            bool custom=p.Mode=="Custom"&&p.IconFollowTheme;
            Color accent=custom?Parse(p.Accent):Stock(key);
            Color foreground=custom?Parse(p.Foreground):Color.FromArgb(242,243,245);
            Bitmap bitmap=new Bitmap(size,size,PixelFormat.Format32bppArgb);
            try {
                using(Graphics g=Graphics.FromImage(bitmap)) {
                    g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;
                    g.PixelOffsetMode=PixelOffsetMode.HighQuality;
                    g.ScaleTransform(size/24f,size/24f);
                    bool frame=p.IconFamily=="ReFIX";
                    using(Pen pen=new Pen(p.IconFamily=="Outline"?foreground:accent,1.85f)) {
                        pen.StartCap=LineCap.Round;pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                        if(frame) {
                            Color border=custom?accent:Stock("hub");
                            PointF[] diamond={new PointF(12,1.5f),new PointF(22.5f,12),new PointF(12,22.5f),new PointF(1.5f,12)};
                            using(SolidBrush fill=new SolidBrush(Color.FromArgb(96,border)))g.FillPolygon(fill,diamond);
                            using(Pen edge=new Pen(border,1.6f)){edge.LineJoin=LineJoin.Round;g.DrawPolygon(edge,diamond);}
                            g.TranslateTransform(4.25f,4.25f);g.ScaleTransform(15.5f/24f,15.5f/24f);pen.Color=foreground;pen.Width=2.5f;
                        }
                        DrawGlyph(g,pen,key,frame);
                    }
                }
                return bitmap;
            } catch {bitmap.Dispose();throw;}
        }
        static Color Parse(string hex){return Color.FromArgb(Convert.ToInt32(hex.Substring(1,2),16),Convert.ToInt32(hex.Substring(3,2),16),Convert.ToInt32(hex.Substring(5,2),16));}
        static Color Stock(string id) {
            switch(id){case "quiet-mixer":return Color.FromArgb(114,200,255);case "task-menu":return Color.FromArgb(174,154,255);case "quick-note":return Color.FromArgb(244,203,114);case "mini-icons":return Color.FromArgb(116,221,197);default:return Color.FromArgb(242,82,96);}
        }
        static void Line(Graphics g,Pen p,float x1,float y1,float x2,float y2){g.DrawLine(p,x1,y1,x2,y2);}
        static void DrawGlyph(Graphics g,Pen p,string id,bool framed) {
            switch(id) {
                case "hub":
                    if(!framed)g.DrawPolygon(p,new PointF[]{new PointF(12,2),new PointF(22,12),new PointF(12,22),new PointF(2,12)});
                    Line(g,p,8,7,8,17);Line(g,p,16,7,16,17);Line(g,p,8,12,16,12);break;
                case "quiet-mixer":
                    Line(g,p,6,4,6,20);Line(g,p,12,4,12,20);Line(g,p,18,4,18,20);
                    Line(g,p,3,8,9,8);Line(g,p,9,15,15,15);Line(g,p,15,10,21,10);break;
                case "task-menu":
                    g.DrawRectangle(p,4,4,6,6);g.DrawRectangle(p,14,4,6,6);g.DrawRectangle(p,4,14,6,6);g.DrawRectangle(p,14,14,6,6);break;
                case "quick-note":
                    g.DrawPolygon(p,new PointF[]{new PointF(5,3),new PointF(15,3),new PointF(20,8),new PointF(20,21),new PointF(5,21)});
                    Line(g,p,15,3,15,8);Line(g,p,15,8,20,8);Line(g,p,8,12,16,12);Line(g,p,8,16,14,16);break;
                case "mini-icons":
                    Line(g,p,3,5,8,5);Line(g,p,3,12,8,12);Line(g,p,3,19,8,19);Line(g,p,13,5,20,12);Line(g,p,20,12,13,19);break;
                default:
                    // A tool/wrench silhouette remains useful for unknown module IDs.
                    g.DrawLines(p,new PointF[]{new PointF(17,3),new PointF(13,7),new PointF(17,11),new PointF(21,7)});
                    g.DrawArc(p,12,2,10,10,0,180);Line(g,p,13,10,4,19);Line(g,p,4,19,6,21);Line(g,p,6,21,15,12);break;
            }
        }
    }
}
