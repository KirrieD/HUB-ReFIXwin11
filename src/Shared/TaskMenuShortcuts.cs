using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
namespace ReFix.Shared
{
    // Own the shortcut file, preserving its native target, arguments, icon and working directory.
    // Documents and folders remain references; importing never executes or deletes a source.
    public sealed class TaskMenuShortcuts
    {
        readonly string directory;
        const int MaximumBytes=4*1024*1024;
        public TaskMenuShortcuts(string root){directory=Path.Combine(Path.GetFullPath(root),"task-menu-shortcuts");}
        public static bool IsShortcut(string path){string extension=Path.GetExtension(path);return string.Equals(extension,".lnk",StringComparison.OrdinalIgnoreCase)||string.Equals(extension,".url",StringComparison.OrdinalIgnoreCase);}
        bool Owned(string path){return path.StartsWith(directory+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);}
        static void Plain(string path){if((File.Exists(path)||Directory.Exists(path))&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("Папка ярлыков содержит перенаправление: "+path);}
        public string Import(string source)
        {
            string path=TaskMenuConfig.NormalizePath(source);if(path==null)throw new ArgumentException("Некорректный путь ярлыка.");
            if(!IsShortcut(path)||Directory.Exists(path))return path;
            if(Owned(path)){Plain(directory);Plain(Path.GetDirectoryName(path));Plain(path);return path;}
            byte[] bytes;using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))
            {if(input.Length>MaximumBytes)throw new IOException("Ярлык превышает 4 МиБ.");using(var reader=new BinaryReader(input)){bytes=reader.ReadBytes((int)input.Length);if(bytes.Length!=input.Length)throw new IOException("Ярлык изменён во время копирования.");}}
            string hash;using(var sha=SHA256.Create())hash=BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();
            Plain(directory);Directory.CreateDirectory(directory);string folder=Path.Combine(directory,hash);Plain(folder);Directory.CreateDirectory(folder);
            string destination=Path.Combine(folder,Path.GetFileName(path));Plain(destination);
            if(File.Exists(destination))return destination;
            string temporary=Path.Combine(folder,Guid.NewGuid().ToString("N")+".tmp");
            try{using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))output.Write(bytes,0,bytes.Length);File.Move(temporary,destination);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            return destination;
        }
        public bool Migrate(TaskMenuConfig config)
        {
            var changes=new List<Tuple<TaskMenuPanel,string,string>>();
            foreach(var panel in config.Panels)foreach(string path in panel.Items)
            {if(!IsShortcut(path)||!File.Exists(path)||Owned(path))continue;string copy=Import(path);if(copy!=path)changes.Add(Tuple.Create(panel,path,copy));}
            foreach(var change in changes){var panel=change.Item1;int index=panel.Items.IndexOf(change.Item2);panel.Items[index]=change.Item3;TaskMenuCell cell;if(panel.Cells.TryGetValue(change.Item2,out cell)){panel.Cells.Remove(change.Item2);panel.Cells[change.Item3]=cell;}}
            return changes.Count>0;
        }
    }
}
