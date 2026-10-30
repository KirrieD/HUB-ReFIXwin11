using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using ReFix.Shared;
namespace ReFix.Tests {
 internal static class TaskMenuOwnershipTests {
  internal static void Run(Action<string,bool> report){
   var assembly=typeof(TaskMenuPanel).Assembly;var type=assembly.GetType("ReFix.Shared.TaskMenuShortcuts");
   report("task menu owns imported shortcuts independently",type!=null);
   string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"menu-ownership-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
   try{if(type!=null){
    var importer=Activator.CreateInstance(type,new object[]{root});var import=type.GetMethod("Import");Func<string,string> copy=p=>(string)import.Invoke(importer,new object[]{p});
    string original=Path.Combine(root,"Website.url");File.WriteAllText(original,"[InternetShortcut]\r\nURL=https://example.com/first\r\n");string contents=File.ReadAllText(original),owned=copy(original);
    report("URL shortcut is copied with its display name and content",owned!=original&&Path.GetFileName(owned)=="Website.url"&&File.ReadAllText(owned)==contents);
    report("adding same shortcut reuses owned copy",copy(original)==owned&&copy(owned)==owned);
    File.WriteAllText(original,"[InternetShortcut]\r\nURL=https://example.com/changed\r\n");File.Move(original,Path.Combine(root,"Renamed.url"));
    report("editing and moving original does not change menu shortcut",File.Exists(owned)&&File.ReadAllText(owned)==contents);
    string regular=Path.Combine(root,"document.txt");File.WriteAllText(regular,"sample");report("regular files remain references",copy(regular)==regular&&copy(root)==root);
    string link=Path.Combine(root,"Application.lnk");CreateShortcut(link,root);byte[] bytes=File.ReadAllBytes(link);string saved=copy(link);File.Delete(link);
    report("LNK stays available after original deletion with exact shortcut data",File.Exists(saved)&&Convert.ToBase64String(bytes)==Convert.ToBase64String(File.ReadAllBytes(saved)));
    VerifyShortcut(saved,root,report);
    var config=new TaskMenuConfig();var panel=new TaskMenuPanel{Monitor="A",Edge=TaskMenuEdge.Top};string legacy=Path.Combine(root,"Legacy.url");File.WriteAllText(legacy,contents);panel.Items.Add(legacy);panel.Items.Add(regular);TaskMenuGrid.Configure(panel,4,3);TaskMenuGrid.Move(panel,legacy,3,2);config.Panels.Add(panel);
    bool migrated=(bool)type.GetMethod("Migrate").Invoke(importer,new object[]{config});string adopted=panel.Items[0];
    report("existing shortcuts migrate without losing grid cells",migrated&&adopted!=legacy&&panel.Cells[adopted].Column==3&&panel.Cells[adopted].Row==2&&panel.Items[1]==regular&&File.Exists(legacy));
    report("shortcut migration is idempotent",!(bool)type.GetMethod("Migrate").Invoke(importer,new object[]{config}));
    var store=new TaskMenuStore(root);store.Save(config);File.Delete(legacy);report("owned shortcut survives configuration reload",File.Exists(store.Load().Panels[0].Items[0]));
   }
   var position=typeof(TaskMenuPanel).GetProperty("Position");report("panel stores its position along the selected edge",position!=null);
   if(position!=null){var monitor=new Rect(-1600,0,1600,900);var work=new Rect(-1600,0,1600,860);
    foreach(TaskMenuEdge edge in Enum.GetValues(typeof(TaskMenuEdge))){var p=new TaskMenuPanel{Monitor="A",Edge=edge,Columns=3,Rows=2,GridConfigured=true};bool vertical=edge==TaskMenuEdge.Left||edge==TaskMenuEdge.Right;
     position.SetValue(p,0d,null);Rect start=TaskMenuGeometry.Expanded(monitor,work,p,1.5);position.SetValue(p,1d,null);Rect end=TaskMenuGeometry.Expanded(monitor,work,p,1.5);
     report("panel moves only along "+edge+" edge",vertical?start.Left==end.Left&&start.Top==work.Top&&end.Bottom==work.Bottom:start.Top==end.Top&&start.Left==work.Left&&end.Right==work.Right);
     var c=new TaskMenuConfig();c.Panels.Add(p);var store=new TaskMenuStore(root);store.Save(c);report("panel edge position survives restart "+edge,(double)position.GetValue(store.Load().Panels[0],null)==1d);
    }
    var invalid=new TaskMenuPanel{Monitor="A"};position.SetValue(invalid,double.NaN,null);var safe=new TaskMenuConfig();safe.Panels.Add(invalid);safe.Normalize();report("invalid panel position resets to center",(double)position.GetValue(invalid,null)==.5);
    var old=new TaskMenuPanel();report("legacy panels default to centered position",(double)position.GetValue(old,null)==.5);
    report("drag positions clamp to ends and tolerate full span panels",TaskMenuGeometry.PositionAt(monitor,work,TaskMenuEdge.Bottom,new Rect(-1500,800,100,100),-99999)==0&&TaskMenuGeometry.PositionAt(monitor,work,TaskMenuEdge.Right,new Rect(-100,0,100,100),99999)==1&&TaskMenuGeometry.PositionAt(monitor,work,TaskMenuEdge.Top,new Rect(-1600,0,1600,100),99999)==.5);
   }
   }finally{Directory.Delete(root,true);}
  }
  static void CreateShortcut(string path,string root){object shell=null,shortcut=null;try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));dynamic s=shell;shortcut=s.CreateShortcut(path);dynamic link=shortcut;link.TargetPath=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"notepad.exe");link.Arguments="\"example document.txt\"";link.WorkingDirectory=root;link.Description="Owned fixture";link.Save();}finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}}
  static void VerifyShortcut(string path,string root,Action<string,bool> report){object shell=null,shortcut=null;try{shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));dynamic s=shell;shortcut=s.CreateShortcut(path);dynamic link=shortcut;report("copied LNK preserves target arguments and working directory",((string)link.TargetPath).EndsWith("notepad.exe",StringComparison.OrdinalIgnoreCase)&&(string)link.Arguments=="\"example document.txt\""&&string.Equals((string)link.WorkingDirectory,root,StringComparison.OrdinalIgnoreCase));}finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);if(shell!=null)Marshal.FinalReleaseComObject(shell);}}
 }
}
