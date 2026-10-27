using System;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ReFix.Shared;
using ReFix.Hub;
namespace ReFix.Tests
{
    internal static class BehaviorSettingsTests
    {
        internal static void Run(Action<string,bool> report)
        {
            ResumeTests(report);
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"behavior-test-"+Guid.NewGuid().ToString("N"));
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(Path.Combine(root,"controls.json"),"{\"AutoHideHub\":false,\"StartMixerWithHub\":true}");
                using(var controls=new ControlsStore(root))
                using(var themes=new ThemeStore(root))
                {
                    var p=controls.Current;
                    report("legacy behavior retains safe module defaults",!p.AutoHideHub&&p.AutoHideMixer&&p.AutoHideTaskMenu&&!p.StartTaskMenuWithHub&&p.StartMixerWithHub);
                    p.AutoHideMixer=false;p.AutoHideTaskMenu=false;p.StartTaskMenuWithHub=true;
                    controls.Save(p);var clone=p.Clone();clone.AutoHideMixer=true;
                    controls.ResetDefaults();
                    using(var reopened=new ControlsStore(root))report("behavior survives save clone and key reset",!reopened.Current.AutoHideMixer&&!reopened.Current.AutoHideTaskMenu&&reopened.Current.StartTaskMenuWithHub&&reopened.Current.StartMixerWithHub&&!reopened.Current.AutoHideHub);
                    report("task menu has distinct global and contextual shortcuts",controls.Current.Get("task-menu.toggle")=="Ctrl+Alt+M"&&controls.Current.Get("task-menu.hide")=="Esc");
                    bool startup=false;
                    var window=new BehaviorWindow(themes,controls,delegate{return startup;},delegate(bool value){startup=value;});
                    try
                    {
                        window.Show();window.UpdateLayout();
                        var check=Find(window,"Скрывать Микшер");check.IsChecked=true;check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        report("behavior checkbox saves immediately",controls.Current.AutoHideMixer);
                        check=Find(window,"Автозапуск HUB");check.IsChecked=true;check.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                        report("HUB startup uses isolated registration adapter",startup);
                        report("behavior exposes exactly six choices",Count(window)==6);
                    }
                    finally{window.Close();}
                }
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        static void ResumeTests(Action<string,bool> report)
        {
            var p=new ControlsProfile{StartMixerWithHub=true,StartTaskMenuWithHub=true};
            var mixer=new ModuleView{Manifest=new ModuleManifest{Id="quiet-mixer"},State="OFF"};
            var menu=new ModuleView{Manifest=new ModuleManifest{Id="task-menu"},State="OFF"};
            var starts=new List<string>();
            Func<ModuleView,Task> start=delegate(ModuleView view){starts.Add(view.Manifest.Id);return Task.FromResult(0);};
            Program.ResumeEnabledModules(p,new[]{mixer,menu},start).GetAwaiter().GetResult();
            report("startup resumes mixer and menu independently",starts.Count==2&&starts[0]=="quiet-mixer"&&starts[1]=="task-menu");
            starts.Clear();p.StartMixerWithHub=false;Program.ResumeEnabledModules(p,new[]{mixer,menu},start).GetAwaiter().GetResult();
            report("menu startup does not require mixer startup",starts.Count==1&&starts[0]=="task-menu");
            starts.Clear();p.StartMixerWithHub=true;menu.State="NO";mixer.State="ON";Program.ResumeEnabledModules(p,new[]{mixer,menu},start).GetAwaiter().GetResult();
            Program.ResumeEnabledModules(p,new ModuleView[0],start).GetAwaiter().GetResult();
            report("startup ignores missing uninstalled and active modules",starts.Count==0);
            mixer.State="OFF";menu.State="OFF";
            try{Program.ResumeEnabledModules(p,new[]{mixer,menu},delegate(ModuleView view){starts.Add(view.Manifest.Id);if(view==mixer)throw new IOException("isolated startup failure");return Task.FromResult(0);}).GetAwaiter().GetResult();}catch(AggregateException){}
            report("one failed startup still attempts the other module",starts.Count==2&&starts[1]=="task-menu");
        }
#if BEHAVIOR_STANDALONE
        [STAThread] public static int Main(){int failed=0;Run(delegate(string name,bool ok){Console.WriteLine((ok?"PASS ":"FAIL ")+name);if(!ok)failed++;});return failed==0?0:1;}
#endif
        static CheckBox Find(DependencyObject root,string text){var c=root as CheckBox;if(c!=null&&(string)c.Content==text)return c;for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++){var found=Find(System.Windows.Media.VisualTreeHelper.GetChild(root,i),text);if(found!=null)return found;}return null;}
        static int Count(DependencyObject root){int count=root is CheckBox?1:0;for(int i=0;i<System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);i++)count+=Count(System.Windows.Media.VisualTreeHelper.GetChild(root,i));return count;}
    }
}
