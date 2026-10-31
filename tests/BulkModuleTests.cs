using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using ReFix.Hub;
using ReFix.Shared;
namespace ReFix.Tests
{
    internal static class BulkModuleTests
    {
        static ModuleView View(string id,string installed,string available){var v=new ModuleView{Manifest=new ModuleManifest{Id=id,Name=id,Version=available,Kind="process",EntryPoint="app.exe",Preview="notes"}};v.UpdateState(installed==""?"NO":"OFF",installed,"");return v;}
        static T Field<T>(object owner,string name){return (T)owner.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);}
        static void Click(CheckBox box){box.IsChecked=box.IsChecked!=true;box.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));}
        static void Pump(){var frame=new DispatcherFrame();Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.ApplicationIdle,new Action(()=>frame.Continue=false));Dispatcher.PushFrame(frame);}
        internal static void Run(Action<string,bool> report)
        {
            report("selection aggregate distinguishes none some and all",BulkModuleActions.SelectionState(0,0)==false&&BulkModuleActions.SelectionState(0,3)==false&&BulkModuleActions.SelectionState(1,3)==null&&BulkModuleActions.SelectionState(3,3)==true);
            var absent=View("absent","","2.0.0");var old=View("old","1.0.0","2.0.0");var current=View("current","2.0.0","2.0.0");var newer=View("newer","3.0.0","2.0.0");
            report("bulk install update and delete use actual installation state",BulkModuleActions.Eligible(absent,BulkOperation.Install)&&!BulkModuleActions.Eligible(absent,BulkOperation.Remove)&&!BulkModuleActions.Eligible(absent,BulkOperation.Update)&&BulkModuleActions.Eligible(old,BulkOperation.Update)&&BulkModuleActions.Eligible(current,BulkOperation.Remove)&&!BulkModuleActions.Eligible(current,BulkOperation.Update)&&!BulkModuleActions.Eligible(newer,BulkOperation.Update));
            old.IsBusy=true;report("bulk skips busy module",!BulkModuleActions.Eligible(old,BulkOperation.Update));old.IsBusy=false;
            var second=View("second","1.0.0","2.0.0");var calls=new List<string>();var result=BulkModuleActions.RunAsync(new[]{old,old,current,second},BulkOperation.Update,v=>{calls.Add(v.Manifest.Id);if(v==old)throw new IOException("fixture failure");return Task.FromResult(0);}).GetAwaiter().GetResult();
            report("batch continues after an error skips current versions and deduplicates",calls.SequenceEqual(new[]{"old","second"})&&result.Completed==1&&result.Errors.Count==1&&result.Skipped==1);
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bulk-ui-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
            try
            {
                string catalog=Path.Combine(root,"catalog.json");File.WriteAllText(catalog,"{\"SchemaVersion\":1,\"Modules\":[]}");
                using(var themes=new ThemeStore(root))using(var controls=new ControlsStore(root))using(var manager=new ModuleManager(root,catalog))
                {
                    var hub=new HubWindow(themes,manager,controls);try
                    {
                        var all=Field<CheckBox>(hub,"selectAll");var install=Field<Button>(hub,"batchInstall");var update=Field<Button>(hub,"batchUpdate");var remove=Field<Button>(hub,"batchRemove");var cards=Field<List<ModuleCard>>(hub,"views");
                        report("empty selection disables all batch actions",all.IsChecked==false&&!install.IsEnabled&&!update.IsEnabled&&!remove.IsEnabled);
                        Click(Field<CheckBox>(cards[0],"selection"));report("single selection fills aggregate square",all.IsChecked==null&&install.IsEnabled&&!update.IsEnabled&&!remove.IsEnabled);
                        Click(all);report("aggregate click selects every visible card",all.IsChecked==true&&cards.All(c=>c.IsSelected));
                        foreach(var view in manager.Modules)view.UpdateState("OFF",view.Manifest.Version,root);Pump();
                        report("fully installed current selection disables install and update",Convert.ToString(install.Content)=="Установлено"&&!install.IsEnabled&&!update.IsEnabled&&remove.IsEnabled);
                        cards[0].View.InstalledVersion="0.0.1";Pump();report("available update enables group update for stopped modules",update.IsEnabled);
                        Click(all);report("aggregate clears selection",all.IsChecked==false&&!install.IsEnabled&&!update.IsEnabled&&!remove.IsEnabled);
                        var search=Field<TextBox>(hub,"search");search.Text=cards[0].View.Manifest.Name;Click(all);report("select all applies to current filtered list",all.IsChecked==true&&cards.Where(c=>c.Visibility==Visibility.Visible).All(c=>c.IsSelected)&&cards.Where(c=>c.Visibility!=Visibility.Visible).All(c=>!c.IsSelected));
                        search.Text="";Click(all);var visual=(FrameworkElement)hub.Content;visual.Measure(new Size(1060,720));visual.Arrange(new Rect(0,0,1060,720));visual.UpdateLayout();var bitmap=new System.Windows.Media.Imaging.RenderTargetBitmap(1060,720,96,96,System.Windows.Media.PixelFormats.Pbgra32);bitmap.Render(visual);var png=new System.Windows.Media.Imaging.PngBitmapEncoder();png.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(bitmap));using(var output=File.Create(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"bulk-actions.png")))png.Save(output);
                    }
                    finally{hub.Exit();}
                }
            }
            finally{Directory.Delete(root,true);}
        }
    }
}
