using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using ReFix.Shared;
using ReFix.Modules;

namespace ReFix.QuickNote
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            bool created;
            using(Mutex mutex=new Mutex(true,"Local\\"+ModuleRuntime.PipeName("quick-note"),out created))
            {
                if(!created) return;
                Paths.Ensure();
                Application app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
                using(ThemeStore themes=new ThemeStore(Paths.Root))
                using(ControlsStore controls=new ControlsStore(Paths.Root))
                using(HwndSource source=new HwndSource(new HwndSourceParameters(ModuleRuntime.PipeName("quick-note")+".Keys"){Width=0,Height=0,WindowStyle=unchecked((int)0x80000000)}))
                {
                    using(NoteWindow window=new NoteWindow(themes.Current,controls))
                    {
                    Action show=delegate { window.Show(); window.Activate(); };
                    using(GlobalKeyRegistration keys=new GlobalKeyRegistration(controls,"quick-note.toggle",source.Handle,0x5200,delegate{if(window.IsVisible){window.Save();window.Hide();}else show();}))
                    {
                    source.AddHook(delegate(IntPtr h,int msg,IntPtr w,IntPtr l,ref bool handled){handled=keys.ProcessMessage(msg,w);return IntPtr.Zero;});
                    EventHandler changed=delegate { app.Dispatcher.BeginInvoke(new Action(delegate { window.ApplyTheme(themes.Current); })); };
                    themes.Changed+=changed;
                    using(ModuleRuntime runtime=new ModuleRuntime("quick-note",
                        delegate { app.Dispatcher.BeginInvoke(show); },
                        delegate { app.Dispatcher.BeginInvoke(new Action(delegate { window.Save(); app.Shutdown(); })); }))
                    {
                        runtime.Start();
                        app.Exit+=delegate { window.Save(); };
                        if(Array.IndexOf(args,"--background")<0) app.Dispatcher.BeginInvoke(show);
                        app.Run();
                    }
                    themes.Changed-=changed;
                    }}
                }
            }
        }
    }
    internal sealed class NoteWindow : Window, IDisposable
    {
        private readonly TextBox editor=new TextBox();
        private readonly TextBlock status=new TextBlock();
        private readonly string path=Path.Combine(Paths.Root,"notes","quick-note.txt");
        private bool dirty;
        private ThemedSurface surface;
        private ThemeProfile current;
        readonly ControlsStore controls;readonly bool ownsControls;readonly EventHandler controlsChanged;bool disposed;
        internal NoteWindow(ThemeProfile profile) : this(profile,null) {}
        internal NoteWindow(ThemeProfile profile,ControlsStore controls)
        {
            this.controls=controls??new ControlsStore(Paths.Root);ownsControls=controls==null;controlsChanged=delegate{Dispatcher.BeginInvoke(new Action(UpdateControlsHelp));};this.controls.Changed+=controlsChanged;
            Title="Быстрая заметка v"+ProductInfo.Version+" · ReFIX"; Width=390; Height=420;
            WindowStyle=WindowStyle.None; AllowsTransparency=true; ResizeMode=ResizeMode.NoResize;
            MinWidth=280; MinHeight=260; WindowStartupLocation=WindowStartupLocation.CenterScreen;
            current=profile;
            ThemeApplier.Apply(this,current); Background=System.Windows.Media.Brushes.Transparent;
            SizeChanged+=delegate { ApplyTheme(current); };
            Grid panel=new Grid { Margin=new Thickness(18) };
            panel.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            panel.RowDefinitions.Add(new RowDefinition());
            panel.RowDefinitions.Add(new RowDefinition { Height=GridLength.Auto });
            StackPanel title=new StackPanel { Margin=new Thickness(0,0,0,16) };
            title.Children.Add(new TextBlock { Text="Быстрая заметка",FontSize=22 });
            title.Children.Add(new TextBlock { Text="v"+ProductInfo.Version,FontSize=12,Opacity=.6,Margin=new Thickness(0,4,0,0) });
            DockPanel header=new DockPanel();
            Button hide=new Button { Content="×", Width=28,Height=28,ToolTip="Сохранить и скрыть",Margin=new Thickness(8,0,0,12) };
            hide.SetResourceReference(Control.BackgroundProperty,"Hub.Surface"); hide.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");
            hide.Click+=delegate { Save(); Hide(); }; DockPanel.SetDock(hide,Dock.Right); header.Children.Add(hide); header.Children.Add(title);
            header.MouseLeftButtonDown+=delegate { if(Mouse.LeftButton==MouseButtonState.Pressed) DragMove(); }; panel.Children.Add(header);
            editor.AcceptsReturn=true; editor.AcceptsTab=true; editor.TextWrapping=TextWrapping.Wrap;
            editor.VerticalScrollBarVisibility=ScrollBarVisibility.Auto; editor.Padding=new Thickness(12);
            editor.SetResourceReference(Control.BackgroundProperty,"Hub.Surface");
            editor.SetResourceReference(Control.ForegroundProperty,"Hub.Foreground");
            editor.SetResourceReference(Control.BorderBrushProperty,"Hub.Border");
            editor.SetResourceReference(Control.FontSizeProperty,"Hub.FontSize");
            editor.MaxLength=1000000;
            if(File.Exists(path)) { try { editor.Text=File.ReadAllText(path,Encoding.UTF8); } catch(IOException) { status.Text="Не удалось прочитать заметку."; } }
            editor.TextChanged+=delegate { dirty=true;UpdateControlsHelp(); };
            Grid.SetRow(editor,1); panel.Children.Add(editor);
            status.Margin=new Thickness(0,12,0,0); UpdateControlsHelp();
            status.SetResourceReference(TextBlock.ForegroundProperty,"Hub.Muted");
            Grid.SetRow(status,2); panel.Children.Add(status);
            surface=new ThemedSurface(panel); Content=surface; ApplyTheme(current);
            Closing+=delegate(object sender,System.ComponentModel.CancelEventArgs e) { if(!disposed){e.Cancel=true; Save(); Hide();} };
            Deactivated+=delegate { Save(); };
            PreviewKeyDown+=delegate(object sender,KeyEventArgs e)
            {
                if(KeyChord.Parse(this.controls.Current.Get("quick-note.hide")).Matches(e.Key==Key.System?e.SystemKey:e.Key,Keyboard.Modifiers)) { Save(); Hide(); e.Handled=true; }
                if(KeyChord.Parse(this.controls.Current.Get("quick-note.save")).Matches(e.Key==Key.System?e.SystemKey:e.Key,Keyboard.Modifiers)) { Save(); e.Handled=true; }
            };
        }
        void UpdateControlsHelp(){if(disposed)return;string save=controls.Current.Get("quick-note.save"),hide=controls.Current.Get("quick-note.hide");status.Text=(dirty?"Изменено":"Сохранено")+(save!=""?" · "+save+" — сохранить":"")+(hide!=""?" · "+hide+" — скрыть":"");}
        public void Dispose(){if(disposed)return;Save();disposed=true;controls.Changed-=controlsChanged;if(ownsControls)controls.Dispose();Close();}
        internal void ApplyTheme(ThemeProfile profile) { current=profile; if(surface!=null) surface.Apply(this,current); }
        internal void Save()
        {
            if(!dirty) return;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
                File.WriteAllText(temp,editor.Text,Encoding.UTF8);
                if(File.Exists(path)) File.Replace(temp,path,null); else File.Move(temp,path);
                dirty=false;UpdateControlsHelp();
            }
            catch(Exception ex)
            {
                if(!(ex is IOException || ex is UnauthorizedAccessException)) throw;
                status.Text="Не удалось сохранить: "+ex.Message;
            }
        }
    }
}
