using System;
using System.IO;
using System.Windows;
using ReFix.Shared;
namespace ReFix.Tests {
 internal static class TaskMenuLayerTests {
  internal static void Run(Action<string,bool> report){
   var profile=new ControlsProfile();var layer=typeof(ControlsProfile).GetProperty("TaskMenuLayer");var edges=typeof(ControlsProfile).GetProperty("ShowTaskMenuEdges");
   report("menu layer and edge visibility are separate persisted preferences",layer!=null&&edges!=null);if(layer==null||edges==null)return;
   report("legacy menu defaults to above all with visible edges",Convert.ToInt32(layer.GetValue(profile,null))==0&&(bool)edges.GetValue(profile,null));
   string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"layer-settings-"+Guid.NewGuid().ToString("N"));
   try{using(var store=new ControlsStore(root)){layer.SetValue(profile,Enum.ToObject(layer.PropertyType,2),null);edges.SetValue(profile,false,null);store.Save(profile);store.ResetDefaults();using(var reopened=new ControlsStore(root)){report("key reset and reload retain menu layering and hidden edges",Convert.ToInt32(layer.GetValue(reopened.Current,null))==2&&!(bool)edges.GetValue(reopened.Current,null));}}
   }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
   var policy=typeof(TaskMenuPanel).Assembly.GetType("ReFix.Shared.TaskMenuLayerPolicy");report("fullscreen layering policy exists",policy!=null);if(policy==null)return;
   var method=policy.GetMethod("Fullscreen");Func<Rect,Rect,bool,bool> full=(monitor,window,maximized)=>(bool)method.Invoke(null,new object[]{monitor,window,maximized});var screen=new Rect(-1920,0,1920,1080);
   report("borderless window covering monitor is fullscreen",full(screen,new Rect(-1920,0,1920,1080),false));
   report("maximized work area yields to window",full(screen,new Rect(-1928,-8,1936,1056),true));
   report("ordinary and other-monitor windows do not suppress menu",!full(screen,new Rect(-1800,100,900,600),false)&&!full(screen,new Rect(0,0,1920,1080),true));
  }
 }
}
