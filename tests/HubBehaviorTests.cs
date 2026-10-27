using System;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReFix.Hub;
using ReFix.Shared;
namespace ReFix.Tests
{
    internal static class HubBehaviorTests
    {
        internal static void Run(Action<string,bool> report)
        {
            MethodInfo policy=typeof(Program).GetMethod("ShouldAutoHide",BindingFlags.NonPublic|BindingFlags.Static);
            report("auto-hide policy is explicit and configurable",policy!=null);
            if(policy!=null)
            {
                Func<bool,uint,uint,bool> check=delegate(bool enabled,uint foreground,uint own){return (bool)policy.Invoke(null,new object[]{enabled,foreground,own});};
                report("disabled auto-hide keeps HUB open on foreground change",!check(false,20,10));
                report("auto-hide only hides for another valid process",check(true,20,10)&&!check(true,10,10)&&!check(true,0,10));
            }
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"hub-behavior-"+Guid.NewGuid().ToString("N"));
            string old=Environment.GetEnvironmentVariable("REFIX_DATA_ROOT");Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",root);
            try
            {
                using(var themes=new ThemeStore(root))
                using(var controls=new ControlsStore(root))
                using(var manager=new ModuleManager(root,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"catalog.json")))
                {
                    var hub=new HubWindow(themes,manager,controls);
                    try
                    {
                        hub.Show();hub.UpdateLayout();
                        hub.OpenBehavior();hub.BehaviorEditor.UpdateLayout();
                        CheckBox auto=FindCheck(hub.BehaviorEditor,"Скрывать HUB");
                        report("behavior settings expose HUB automatic hiding checkbox",auto!=null);
                        if(auto!=null)
                        {
                            auto.IsChecked=false;auto.RaiseEvent(new RoutedEventArgs(CheckBox.ClickEvent));
                            var property=typeof(ControlsProfile).GetProperty("AutoHideHub");
                            using(var reload=new ControlsStore(root))report("auto-hide preference persists across launches",property!=null&&!(bool)property.GetValue(reload.Current,null));
                        }
                        foreach(var view in manager.Modules)
                        {
                            var card=new ModuleCard(view,manager,delegate{},controls);card.Measure(new Size(700,200));card.Arrange(new Rect(0,0,700,200));card.UpdateLayout();
                            try
                            {
                                Button remove=FindButton(card,"Удалить утилиту");
                                report("uninstalled module does not offer deletion: "+view.Manifest.Id,remove!=null&&remove.Visibility==Visibility.Collapsed);
                                view.InstalledVersion=view.Manifest.Version;view.State="OFF";card.Update();
                                report("installed module offers deletion: "+view.Manifest.Id,remove!=null&&remove.Visibility==Visibility.Visible&&remove.IsEnabled);
                                view.IsBusy=true;card.Update();
                                report("busy module cannot be deleted: "+view.Manifest.Id,remove!=null&&!remove.IsEnabled);
                            }
                            finally{card.Dispose();}
                        }
                    }
                    finally{hub.Exit();}
                }
            }
            finally{Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",old);if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        static CheckBox FindCheck(DependencyObject root,string caption)
        {
            var match=root as CheckBox;if(match!=null&&Convert.ToString(match.Content)==caption)return match;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=FindCheck(VisualTreeHelper.GetChild(root,i),caption);if(found!=null)return found;}return null;
        }
        static Button FindButton(DependencyObject root,string caption)
        {
            var match=root as Button;if(match!=null&&Convert.ToString(match.Content)==caption)return match;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=FindButton(VisualTreeHelper.GetChild(root,i),caption);if(found!=null)return found;}return null;
        }
    }
}
