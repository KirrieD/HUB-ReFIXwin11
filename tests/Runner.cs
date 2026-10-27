using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;
using ReFix.Shared;
using ReFix.Hub;
namespace ReFix.Tests
{
    internal static class Runner
    {
        private static int passed,failed;
        private static void Report(string name,bool ok){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(ok)passed++;else failed++;}
        private static void Group(string name,Action action){try{action();}catch(Exception ex){Console.WriteLine(ex);Report(name,false);}}
        [STAThread]
        private static int Main()
        {
            Group("shared SDK",delegate{SharedTests.Run(Report);});
            Group("portable startup",delegate{StartupTests.Run(Report);});
            Group("embedded offline module",delegate{EmbeddedModuleTests.Run(Report);});
            Group("scoped custom themes",delegate{ScopedThemeTests.Run(Report);});
            Group("task menu configuration and geometry",delegate{TaskMenuTests.Run(Report);});
            Group("behavior settings",delegate{BehaviorSettingsTests.Run(Report);});
            Group("opaque settings surfaces",delegate{SettingsSurfaceTests.Run(Report);});
            Group("keyboard controls",delegate{ControlsTests.Run(Report);});
            Group("legacy mixer migration",delegate{MigrationTests.Run(Report);});
            Group("bootstrap and setup",delegate{BootstrapTests.Run(Report);});
            Group("verified self update",delegate{UpdateTests.Run(Report);});
            Group("backend",delegate{BackendTests.Run(Report);});
            Group("real module lifecycle",delegate{BackendTests.RunIntegration(Report);});
            Group("modeless editor and live theme",UiTest);
            Group("HUB behavior",delegate{HubBehaviorTests.Run(Report);});
            Group("controls keyboard preview",delegate{ControlsUiTests.Run(Report);});
            Console.WriteLine(passed+" passed; "+failed+" failed");return failed==0?0:1;
        }
        private static void UiTest()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"ui-test-"+Guid.NewGuid().ToString("N"));
            string old=Environment.GetEnvironmentVariable("REFIX_DATA_ROOT");Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",root);
            try
            {
                using(ThemeStore store=new ThemeStore(root))
                using(ModuleManager manager=new ModuleManager(root,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog.json")))
                {
                    HubWindow hub=new HubWindow(store,manager);
                    try
                    {
                        if(hub.StatusTimerRunning)throw new Exception("Hidden HUB must not poll status");
                        hub.Show();hub.OpenStyles();Pump();
                        if(!hub.IsEnabled || hub.StyleEditor==null || !hub.StyleEditor.IsVisible || hub.StyleEditor.Owner!=hub)throw new Exception("Editor must remain modeless while HUB is usable");
                        ComboBox mode=Find<ComboBox>(hub.StyleEditor,null);mode.SelectedIndex=1;Pump();
                        TextBox accent=Find<TextBox>(hub.StyleEditor,"Акцент HEX");accent.Text="#C64072";
                        accent.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,0,accent,hub){RoutedEvent=Keyboard.LostKeyboardFocusEvent});
                        mode.SelectedIndex=0;mode.SelectedIndex=1;
                        if(store.Current.Accent!="#C64072")throw new Exception("Switching System/Custom must preserve the user's custom palette");
                        ThemeProfile p=store.Current.Clone();p.Mode="Custom";p.Accent="#C64072";p.BackgroundOpacity=.4;store.Save(p);
                        DateTime until=DateTime.UtcNow.AddSeconds(3);
                        do{Pump();if(((SolidColorBrush)hub.Resources["Hub.Accent"]).Color.R==198 && ((SolidColorBrush)hub.StyleEditor.Resources["Hub.Accent"]).Color.R==198)break;System.Threading.Thread.Sleep(10);}while(DateTime.UtcNow<until);
                        Color a=((SolidColorBrush)hub.Resources["Hub.Accent"]).Color,b=((SolidColorBrush)hub.StyleEditor.Resources["Hub.Accent"]).Color;
                        if(a.R!=198 || a.G!=64 || a.B!=114 || a!=b)throw new Exception("Theme must update both real windows: HUB="+a+" Editor="+b);
                        if(hub.Opacity!=1 || hub.StyleEditor.Opacity!=1)throw new Exception("Background alpha must not fade text");
                        hub.OpenControls();Pump();if(!hub.IsEnabled||hub.ControlsEditor==null||hub.ControlsEditor.Owner!=hub)throw new Exception("Controls must be modeless");
                        if(((SolidColorBrush)hub.ControlsEditor.Resources["Hub.Accent"]).Color!=a)throw new Exception("Controls must follow current shared theme");
                        TextBox binding=Find<TextBox>(hub.ControlsEditor,"Сочетание: Скрыть HUB");if(binding==null)throw new Exception("Editable binding input missing");binding.Text="Backspace";
                        binding.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(binding),0,Key.Enter){RoutedEvent=Keyboard.KeyDownEvent});
                        using(ControlsStore check=new ControlsStore(root))if(check.Current.Get("hub.hide")!="Backspace")throw new Exception("Typed assignment was not saved");
                        TextBox focusedSearch=Find<TextBox>(hub,"Поиск инструментов");focusedSearch.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice,PresentationSource.FromVisual(focusedSearch),0,Key.Back){RoutedEvent=Keyboard.PreviewKeyDownEvent});
                        if(hub.IsVisible||hub.ControlsEditor.IsVisible)throw new Exception("Reassigned hide must work before text input consumes key; physical modifiers="+Keyboard.Modifiers+"; saved="+((ControlsStore)typeof(HubWindow).GetField("controls",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(hub)).Current.Get("hub.hide"));
                        Report("modeless controls, manual key assignment and preview key routing",true);
                        hub.HideHub();if(hub.StatusTimerRunning || hub.StyleEditor.IsVisible)throw new Exception("Hidden windows must stop UI polling");
                        Report("modeless editor, shared live colors, opaque text, no hidden status timer",true);
                    }
                    finally{hub.Exit();}
                }
            }
            finally{Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",old);if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        private static void Pump(){DispatcherFrame frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));Dispatcher.PushFrame(frame);}
        private static T Find<T>(DependencyObject root,string name) where T:DependencyObject
        {
            T match=root as T;if(match!=null && (name==null || AutomationProperties.GetName(match)==name))return match;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){T found=Find<T>(VisualTreeHelper.GetChild(root,i),name);if(found!=null)return found;}return null;
        }
    }
}
