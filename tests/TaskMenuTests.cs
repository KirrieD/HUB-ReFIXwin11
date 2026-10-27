using System;
using System.IO;
using System.Collections.Generic;
using System.Windows;
using ReFix.Shared;
namespace ReFix.Tests
{
    public static class TaskMenuTests
    {
        public static void Run(Action<string,bool> report)
        {
            var monitor=new Rect(-1920,-200,1920,1080);var work=new Rect(-1920,-200,1920,1040);
            report("task-menu bottom taskbar at negative coordinates",TaskMenuGeometry.TaskbarEdge(monitor,new Rect(-1920,840,1920,40))==TaskMenuEdge.Bottom);
            report("task-menu exact left side",TaskMenuGeometry.TaskbarEdge(monitor,new Rect(-1920,-200,48,1080))==TaskMenuEdge.Left);
            report("task-menu top taskbar",TaskMenuGeometry.TaskbarEdge(monitor,new Rect(-1920,-200,1920,48))==TaskMenuEdge.Top);
            report("task-menu exact right side",TaskMenuGeometry.TaskbarEdge(monitor,new Rect(-48,-200,48,1080))==TaskMenuEdge.Right);
            report("task-menu other monitor taskbar ignored",!TaskMenuGeometry.TaskbarEdge(monitor,new Rect(0,1040,1920,40)).HasValue);
            report("task-menu short overlapping rectangle is not taskbar edge",!TaskMenuGeometry.TaskbarEdge(monitor,new Rect(-1000,840,100,40)).HasValue);
            report("task-menu interior rectangle is not taskbar edge",!TaskMenuGeometry.TaskbarEdge(monitor,new Rect(-1900,500,1900,40)).HasValue);
            report("task-menu auto-hidden taskbar crossing bottom border",TaskMenuGeometry.TaskbarEdge(monitor,new Rect(-1920,878,1920,40))==TaskMenuEdge.Bottom);
            report("task-menu width grows with actual populated columns",TaskMenuGeometry.Expanded(monitor,work,TaskMenuEdge.Top,1,4,40,1).Width<TaskMenuGeometry.Expanded(monitor,work,TaskMenuEdge.Top,4,4,40,1).Width);
            foreach(TaskMenuEdge e in Enum.GetValues(typeof(TaskMenuEdge))){var full=TaskMenuGeometry.Expanded(monitor,work,e,512,12,96,1.5);var sliver=TaskMenuGeometry.Sliver(full,e,6);report("task-menu bounded expanded and sliver "+e,monitor.Contains(full)&&full.Contains(sliver)&&Math.Min(sliver.Width,sliver.Height)==6);}
            var c=new TaskMenuConfig();foreach(TaskMenuEdge e in Enum.GetValues(typeof(TaskMenuEdge)))c.Panels.Add(new TaskMenuPanel{Monitor="A",Edge=e});c.Panels.Add(new TaskMenuPanel{Monitor="B",Edge=TaskMenuEdge.Top});c.Panels.Add(new TaskMenuPanel{Monitor="B",Edge=TaskMenuEdge.Top});c.Normalize();report("task-menu maximum three per monitor and unique edge",c.Panels.Count==4);
            var p=new TaskMenuPanel{Monitor="C",Columns=999,IconSize=-1};p.Items.Add(@"C:\Temp\Example.txt");p.Items.Add(@"c:\temp\example.txt");p.Items.Add("https://example.org");p.Items.Add("relative.exe");p.Items.Add("C:\\bad\0name");c.Panels.Add(p);c.Normalize();report("task-menu item path validation and dedup",p.Items.Count==1);report("task-menu dimensions normalized",p.Columns==12&&p.IconSize==24);
            report("task-menu rooted current-drive and alternate-stream targets rejected",TaskMenuConfig.NormalizePath(@"\Windows\system32\cmd.exe")==null&&TaskMenuConfig.NormalizePath(@"C:\Temp\file.txt:stream")==null);
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"task-menu-fixture-"+Guid.NewGuid().ToString("N"));try{var store=new TaskMenuStore(root);store.Save(c);var loaded=store.Load();report("task-menu persists only under supplied data root",store.FilePath==Path.Combine(Path.GetFullPath(root),"task-menu.json")&&File.Exists(store.FilePath));report("task-menu independent item lists round trip",loaded.Panels.Count==5&&loaded.Panels[4].Items.Count==1&&loaded.Panels[0].Items.Count==0);File.WriteAllText(store.FilePath,"{\"Panels\":[{\"Monitor\":\"legacy\",\"Edge\":0}]}");var old=store.Load();report("task-menu older config uses constructor defaults",old.Panels.Count==1&&old.Panels[0].Columns==4&&old.Panels[0].IconSize==40&&old.Panels[0].Items.Count==0);File.WriteAllText(store.FilePath,"{broken");report("task-menu malformed config recovers safely",store.Load().Panels.Count==0);}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
