using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
namespace ReFix.Hub
{
    internal sealed class SettingsPlacementResult { internal Rect Owner,Child;internal bool OnLeft; }
    internal static class SettingsPlacement
    {
        internal static SettingsPlacementResult Calculate(Rect work,Rect owner,double width,double height,double minimumWidth,double ownerMinimumWidth)
        {
            const double gap=12;
            bool left=owner.Left-work.Left>work.Right-owner.Right;
            minimumWidth=Math.Min(work.Width,Math.Max(280,minimumWidth));
            double ow=Math.Min(owner.Width,work.Width),oh=Math.Min(owner.Height,work.Height);
            if(ow+minimumWidth+gap>work.Width&&work.Width>=ownerMinimumWidth+minimumWidth+gap)ow=work.Width-minimumWidth-gap;
            double ox=Math.Max(work.Left,Math.Min(owner.Left,work.Right-ow)),oy=Math.Max(work.Top,Math.Min(owner.Top,work.Bottom-oh));
            if(work.Width>=ow+minimumWidth+gap)
            {
                if(left)ox=Math.Max(ox,work.Left+minimumWidth+gap);else ox=Math.Min(ox,work.Right-ow-minimumWidth-gap);
            }
            owner=new Rect(ox,oy,ow,oh);
            double free=left?owner.Left-work.Left-gap:work.Right-owner.Right-gap;
            double cw=Math.Min(work.Width,Math.Max(minimumWidth,Math.Min(width,free))),ch=Math.Min(height,work.Height);
            double cx=left?owner.Left-gap-cw:owner.Right+gap;
            cx=Math.Max(work.Left,Math.Min(cx,work.Right-cw));double cy=Math.Max(work.Top,Math.Min(owner.Top,work.Bottom-ch));
            return new SettingsPlacementResult{Owner=owner,Child=new Rect(cx,cy,cw,ch),OnLeft=left};
        }
        [StructLayout(LayoutKind.Sequential)]struct RECT{internal int Left,Top,Right,Bottom;internal Rect Value{get{return new Rect(Left,Top,Math.Max(1,Right-Left),Math.Max(1,Bottom-Top));}}}
        [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
        [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
        [DllImport("user32.dll")]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
        internal static void Place(Window owner,Window child)
        {
            if(owner==null||!owner.IsVisible)return;child.WindowStartupLocation=WindowStartupLocation.Manual;
            IntPtr parent=new WindowInteropHelper(owner).Handle,window=new WindowInteropHelper(child).EnsureHandle();RECT bounds;if(!GetWindowRect(parent,out bounds))return;
            var screen=System.Windows.Forms.Screen.FromHandle(parent).WorkingArea;
            double scale=1,parentScale=1;try{scale=Math.Max(96,GetDpiForWindow(window))/96d;parentScale=Math.Max(96,GetDpiForWindow(parent))/96d;}catch(EntryPointNotFoundException){}
            var placement=Calculate(new Rect(screen.X,screen.Y,screen.Width,screen.Height),bounds.Value,child.Width*scale,child.Height*scale,Math.Max(420,child.MinWidth)*scale,Math.Max(1,owner.MinWidth)*parentScale);
            if(Math.Abs(placement.Owner.Width-bounds.Value.Width)>.5)owner.Width=placement.Owner.Width/parentScale;
            SetWindowPos(parent,IntPtr.Zero,(int)placement.Owner.X,(int)placement.Owner.Y,(int)placement.Owner.Width,(int)placement.Owner.Height,0x14);
            child.Width=placement.Child.Width/scale;child.Height=placement.Child.Height/scale;
            SetWindowPos(window,IntPtr.Zero,(int)placement.Child.X,(int)placement.Child.Y,(int)placement.Child.Width,(int)placement.Child.Height,0x14);
        }
    }
}
