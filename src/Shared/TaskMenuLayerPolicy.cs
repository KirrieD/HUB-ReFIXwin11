using System;
using System.Windows;
namespace ReFix.Shared
{
    public enum TaskMenuLayer { AboveAll, ExceptFullscreen, BelowWindows }
    public static class TaskMenuLayerPolicy
    {
        public static TaskMenuLayer Normalize(TaskMenuLayer value){return Enum.IsDefined(typeof(TaskMenuLayer),value)?value:TaskMenuLayer.AboveAll;}
        public static bool Topmost(TaskMenuLayer mode,bool fullscreen){return mode==TaskMenuLayer.AboveAll||(mode==TaskMenuLayer.ExceptFullscreen&&!fullscreen);}
        public static bool OnMonitor(Rect monitor,Rect window){return !window.IsEmpty&&(monitor.Contains(new Point(window.Left+window.Width/2,window.Top+window.Height/2))||Fullscreen(monitor,window,false));}
        public static bool Fullscreen(Rect monitor,Rect window,bool maximized)
        {
            if(monitor.IsEmpty||window.IsEmpty||window.Width<=0||window.Height<=0)return false;
            if(maximized&&monitor.Contains(new Point(window.Left+window.Width/2,window.Top+window.Height/2)))return true;
            const double tolerance=2;
            return window.Left<=monitor.Left+tolerance&&window.Top<=monitor.Top+tolerance&&window.Right>=monitor.Right-tolerance&&window.Bottom>=monitor.Bottom-tolerance;
        }
    }
}
