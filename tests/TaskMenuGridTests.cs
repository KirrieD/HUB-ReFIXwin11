using System;
using System.IO;
using ReFix.Shared;
namespace ReFix.Tests
{
    internal static class TaskMenuGridTests
    {
        internal static void Run(Action<string,bool> report)
        {
            foreach(TaskMenuEdge edge in Enum.GetValues(typeof(TaskMenuEdge)))
            {
                var p=Panel(edge);TaskMenuGrid.Ensure(p);bool vertical=edge==TaskMenuEdge.Left||edge==TaskMenuEdge.Right;
                report("grid starts along screen edge "+edge,vertical?p.Columns==1&&p.Rows==3&&p.Cells[p.Items[2]].Row==2:p.Columns==3&&p.Rows==1&&p.Cells[p.Items[2]].Column==2);
            }
            var panel=Panel(TaskMenuEdge.Top);TaskMenuGrid.Configure(panel,5,3);
            report("explicit empty columns and rows determine panel size",panel.Columns==5&&panel.Rows==3&&TaskMenuGrid.Width(panel)==5*(24+16)+28);
            var first=panel.Items[0];var second=panel.Items[1];TaskMenuGrid.Move(panel,first,4,2);
            report("drag moves icon horizontally and vertically and leaves empty cell",panel.Cells[first].Column==4&&panel.Cells[first].Row==2&&panel.Cells[second].Column==1);
            TaskMenuGrid.Move(panel,second,4,2);
            report("drop on occupied block swaps icons",panel.Cells[second].Column==4&&panel.Cells[second].Row==2&&panel.Cells[first].Column==1&&panel.Cells[first].Row==0);
            TaskMenuGrid.Configure(panel,1,1);
            report("shrinking grid reflows icons without losing them",panel.Items.Count==3&&panel.Cells.Count==3&&panel.Columns==1&&panel.Rows==3);
            report("invalid move is rejected without changing layout",!TaskMenuGrid.Move(panel,first,-1,0)&&!TaskMenuGrid.Move(panel,first,12,0)&&!TaskMenuGrid.Move(panel,"missing",0,0));
            var c=new TaskMenuConfig();var low=Panel(TaskMenuEdge.Left);low.IconSize=1;var high=Panel(TaskMenuEdge.Right);high.Monitor="B";high.IconSize=96;c.Panels.Add(low);c.Panels.Add(high);c.Normalize();
            report("icon size is clamped to fourteen through twenty eight",low.IconSize==14&&high.IconSize==28);
            report("panel width maps to whole block columns",TaskMenuGrid.ColumnsForWidth(188,24)==4&&TaskMenuGrid.ColumnsForWidth(0,24)==1);
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"grid-roundtrip-"+Guid.NewGuid().ToString("N"));
            try
            {
                var config=new TaskMenuConfig();config.Panels.Add(panel);TaskMenuGrid.Configure(panel,4,3);TaskMenuGrid.Move(panel,first,3,2);var store=new TaskMenuStore(root);store.Save(config);var loaded=store.Load().Panels[0];
                report("block positions empty cells and dimensions survive restart",loaded.GridConfigured&&loaded.Columns==4&&loaded.Rows==3&&loaded.Cells[first].Column==3&&loaded.Cells[first].Row==2);
                File.WriteAllText(store.FilePath,"{\"Panels\":[{\"Monitor\":\"legacy\",\"Edge\":0,\"IconSize\":40,\"Items\":[\"C:\\\\one.txt\",\"C:\\\\two.txt\"]}]}");var old=store.Load().Panels[0];
                report("legacy menus preserve paths and migrate to edge oriented blocks",old.Items.Count==2&&old.IconSize==28&&old.Columns==1&&old.Cells[old.Items[1]].Row==1);
            }
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
        static TaskMenuPanel Panel(TaskMenuEdge edge){var p=new TaskMenuPanel{Monitor="A",Edge=edge,IconSize=24};p.Items.Add(@"C:\one.txt");p.Items.Add(@"C:\two.txt");p.Items.Add(@"C:\three.txt");return p;}
    }
}
