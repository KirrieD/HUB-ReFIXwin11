using System;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace ReFix.Hub
{
    internal static class NoteShellMenu
    {
        internal const string Extension = ".refixnote";
        internal const string ProgId = "ReFIXHub.QuickNote";
        const string OwnerValue = "ReFIXHubLauncher";
        const string CommandKey = ProgId + @"\shell\open\command";
        const string NewKey = Extension + @"\ShellNew";
        const string IconKey = ProgId + @"\DefaultIcon";

        internal static bool Supported(ModuleView module)
        {
            Version installed;
            return module != null && Version.TryParse(module.InstalledVersion, out installed) && installed >= new Version(1, 6, 3);
        }

        internal static void Sync(string launcher, bool enabled)
        {
            if (!enabled)
            {
                using (var classes = Registry.CurrentUser.OpenSubKey(@"Software\Classes", true))
                    if (classes != null && Sync(classes, launcher, false)) Notify();
                return;
            }
            using (var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes"))
                if (Sync(classes, launcher, true)) Notify();
        }

        internal static bool Sync(RegistryKey classes, string launcher, bool enabled)
        {
            var extensionOwner = Read(classes, Extension, OwnerValue);
            var programOwner = Read(classes, ProgId, OwnerValue);
            if (!enabled)
            {
                bool removed = false;
                if (String.Equals(extensionOwner, launcher, StringComparison.OrdinalIgnoreCase))
                {
                    classes.DeleteSubKeyTree(Extension, false);
                    removed = true;
                }
                if (String.Equals(programOwner, launcher, StringComparison.OrdinalIgnoreCase))
                {
                    classes.DeleteSubKeyTree(ProgId, false);
                    removed = true;
                }
                return removed;
            }

            if ((extensionOwner == null && Exists(classes, Extension)) || (programOwner == null && Exists(classes, ProgId)))
                throw new InvalidOperationException("Расширение .refixnote уже зарегистрировано другой программой.");

            string command = "\"" + launcher + "\" --new-note";
            bool changed = Put(classes, ProgId, OwnerValue, launcher);
            changed |= Put(classes, ProgId, "", "Быстрая заметка");
            changed |= Put(classes, IconKey, "", "\"" + launcher + "\",0");
            changed |= Put(classes, CommandKey, "", command);
            changed |= Put(classes, Extension, OwnerValue, launcher);
            changed |= Put(classes, Extension, "", ProgId);
            changed |= Put(classes, NewKey, "Command", command);
            return changed;
        }

        static bool Exists(RegistryKey parent, string path) { using (var key = parent.OpenSubKey(path)) return key != null; }
        static string Read(RegistryKey parent, string path, string name)
        {
            using (var key = parent.OpenSubKey(path)) return key == null ? null : key.GetValue(name) as string;
        }

        static bool Put(RegistryKey parent, string path, string name, string value)
        {
            if (Read(parent, path, name) == value) return false;
            using (var key = parent.CreateSubKey(path)) key.SetValue(name, value, RegistryValueKind.String);
            return true;
        }

        static void Notify() { SHChangeNotify(0x08000000, 0, IntPtr.Zero, IntPtr.Zero); }
        [DllImport("shell32.dll")] static extern void SHChangeNotify(int eventId, uint flags, IntPtr item1, IntPtr item2);
    }
}
