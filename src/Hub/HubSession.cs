using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Forms.Integration;
using ReFix.Shared;
namespace ReFix.Hub
{
    // Loaded on first request; the background tray does not create WPF.
    internal sealed class HubSession:IHubSession
    {
        private readonly Application app;
        private readonly HubWindow hub;
        private readonly ThemeStore themes;
        internal HubSession(ThemeStore themes,ModuleManager manager,ControlsStore controls,Action checkUpdates)
        {
            this.themes=themes;
            app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
            app.DispatcherUnhandledException+=delegate(object sender,DispatcherUnhandledExceptionEventArgs e){Program.Log(e.Exception);e.Handled=true;System.Windows.Forms.Application.ExitThread();};
            hub=new HubWindow(themes,manager,controls,checkUpdates);ElementHost.EnableModelessKeyboardInterop(hub);
        }
        public bool IsVisible {get{return hub.IsVisible;}}
        public void Show(){hub.ShowHub();}
        public void Toggle(){if(hub.IsVisible)Hide();else Show();}
        public void Hide(){hub.HideHub();}
        public void OpenStyles(){hub.OpenStyles();}
        public void OpenControls(){hub.OpenControls();}
        public void OpenSettings(){hub.OpenSettings();}
        public void SetNotice(string message){hub.SetNotice(message);}
        public void Close(){hub.Exit();app.Shutdown();}
        public void Render(string path)
        {
            Show();OpenStyles();Pump();Directory.CreateDirectory(path);Capture(hub,Path.Combine(path,"hub.png"));Capture(hub.StyleEditor,Path.Combine(path,"styles.png"));
            OpenControls();Pump();Capture(hub.ControlsEditor,Path.Combine(path,"controls.png"));
            hub.OpenBehavior();Pump();Capture(hub.BehaviorEditor,Path.Combine(path,"behavior.png"));
            var selector=new SettingsWindow(themes){Owner=hub};
            try{selector.Show();Pump();Capture(selector,Path.Combine(path,"settings.png"));}finally{selector.Close();}
            var appearance=new SettingsWindow(themes,true){Owner=hub};try{appearance.Show();Pump();Capture(appearance,Path.Combine(path,"appearance.png"));}finally{appearance.Close();}
            hub.OpenLinks();Pump();Capture(hub.LinksEditor,Path.Combine(path,"links.png"));hub.LinksEditor.Close();
            hub.OpenMixerLayout();Pump();Capture(hub.MixerEditor,Path.Combine(path,"mixer-layout.png"));
        }
        private static void Pump(){DispatcherFrame frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);}
        private static void Capture(Window window,string path)
        {
            window.UpdateLayout();RenderTargetBitmap bitmap=new RenderTargetBitmap((int)window.ActualWidth,(int)window.ActualHeight,96,96,PixelFormats.Pbgra32);bitmap.Render((Visual)window.Content);
            PngBitmapEncoder encoder=new PngBitmapEncoder();encoder.Frames.Add(BitmapFrame.Create(bitmap));using(FileStream stream=File.Create(path))encoder.Save(stream);
        }
    }
}
