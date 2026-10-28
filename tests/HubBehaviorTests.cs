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
                        var settingsButton=FindButton(hub,"Настройки");report("HUB settings caption is centered",settingsButton!=null&&settingsButton.HorizontalContentAlignment==HorizontalAlignment.Center);
                        var operation=Find<TextBlock>(hub,delegate(TextBlock t){return System.Windows.Automation.AutomationProperties.GetName(t)=="Состояние обновлений и операций";});
                        report("operation status has prominent wrapping text",operation!=null&&operation.FontSize>=13&&operation.FontWeight==FontWeights.SemiBold&&operation.TextWrapping==TextWrapping.Wrap);
                        report("HUB folder controls are removed from sidebar",Find<TextBox>(hub,delegate(TextBox t){return System.Windows.Automation.AutomationProperties.GetName(t)=="Папка HUB";})==null);
                        hub.OpenLinks();hub.LinksEditor.UpdateLayout();
                        var official=Find<TextBox>(hub.LinksEditor,delegate(TextBox t){return t.Text==SharedSourceStore.OfficialRepository;});
                        var hubPath=Find<TextBox>(hub.LinksEditor,delegate(TextBox t){return t.Text==AppDomain.CurrentDomain.BaseDirectory;});
                        report("links expose immutable official repository and HUB path",official!=null&&official.IsReadOnly&&hubPath!=null&&hubPath.IsReadOnly&&FindButton(hub.LinksEditor,"Открыть папку HUB")!=null);
                        var ownerBounds=Bounds(hub);var linksBounds=Bounds(hub.LinksEditor);
                        var work=System.Windows.Forms.Screen.FromHandle(new System.Windows.Interop.WindowInteropHelper(hub).Handle).WorkingArea;
                        double scale=1;var source=PresentationSource.FromVisual(hub);if(source!=null&&source.CompositionTarget!=null)scale=source.CompositionTarget.TransformToDevice.M11;
                        bool canFit=work.Width>=(hub.MinWidth+hub.LinksEditor.MinWidth)*scale+12;
                        bool beside=linksBounds.Right+8<=ownerBounds.Left||linksBounds.Left>=ownerBounds.Right+8;
                        bool bounded=linksBounds.Left>=work.Left&&linksBounds.Right<=work.Right&&linksBounds.Top>=work.Top&&linksBounds.Bottom<=work.Bottom;
                        report("actual settings sit beside HUB when space permits, otherwise stay visible",canFit?beside:bounded);
                        hub.LinksEditor.Close();
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
                                view.InstalledVersion="0.0.1";view.State="ON";card.Update();var update=FindButton(card,"Обновить");
                                report("active module can be updated without manual stop: "+view.Manifest.Id,update!=null&&update.Visibility==Visibility.Visible&&update.IsEnabled);
                                var version=Find<TextBlock>(card,delegate(TextBlock t){return t.Text.Contains("доступна v");});
                                bool bold=false;if(version!=null)foreach(var inline in version.Inlines){var run=inline as System.Windows.Documents.Run;if(run!=null&&run.Text.Contains("доступна v")&&run.FontSize>=14&&run.FontWeight==FontWeights.Bold)bold=true;}
                                report("available version is larger and bold: "+view.Manifest.Id,bold);
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
        static T Find<T>(DependencyObject root,Func<T,bool> match) where T:DependencyObject{var value=root as T;if(value!=null&&match(value))return value;for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var found=Find<T>(VisualTreeHelper.GetChild(root,i),match);if(found!=null)return found;}return null;}
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]struct RECT{internal int Left,Top,Right,Bottom;}
        [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out RECT rect);
        static Rect Bounds(Window window){RECT r;GetWindowRect(new System.Windows.Interop.WindowInteropHelper(window).Handle,out r);return new Rect(r.Left,r.Top,Math.Max(0,r.Right-r.Left),Math.Max(0,r.Bottom-r.Top));}
    }
}
