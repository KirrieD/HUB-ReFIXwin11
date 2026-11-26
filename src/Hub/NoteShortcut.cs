using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ReFix.Hub
{
    internal static class NoteShortcut
    {
        internal static void Create(string launcher)
        {
            if (!File.Exists(launcher)) throw new FileNotFoundException("Исполняемый файл HUB не найден.", launcher);
            string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            if (String.IsNullOrEmpty(desktop)) throw new DirectoryNotFoundException("Папка рабочего стола недоступна.");
            string file = Path.Combine(desktop, "Новая заметка — HUB ReFIX.lnk");
            dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell", true));
            try
            {
                dynamic link = shell.CreateShortcut(file);
                try
                {
                    if (File.Exists(file) && (string)link.Arguments != "--new-note")
                        throw new IOException("На рабочем столе уже есть ярлык с таким именем.");
                    link.TargetPath = launcher;
                    link.WorkingDirectory = Path.GetDirectoryName(launcher);
                    link.Arguments = "--new-note";
                    link.Description = "Новая заметка в HUB ReFIX";
                    link.Save();
                }
                finally { Marshal.FinalReleaseComObject(link); }
            }
            finally { Marshal.FinalReleaseComObject(shell); }
        }
    }
}
