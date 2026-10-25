using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ReFix.Bootstrap
{
    // Only file operations: isolated tests never invoke installer registry or process code.
    public sealed class InstallFileTransaction
    {
        private string source, target;
        private string[] files;
        private readonly HashSet<string> existed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        public string BackupDirectory { get; private set; }
        public string[] UnknownCollisions { get; private set; }
        public static InstallFileTransaction Prepare(string source, string target, string backupDirectory)
        {
            AuditResult audit = BundleAudit.ValidateDirectory(source, true);
            if (!audit.Success) throw new InvalidDataException(String.Join("\n", audit.Missing));
            InstallFileTransaction plan = new InstallFileTransaction { source = Path.GetFullPath(source), target = Path.GetFullPath(target), BackupDirectory = Path.GetFullPath(backupDirectory) };
            string targetPrefix = plan.target.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (plan.BackupDirectory.StartsWith(targetPrefix, StringComparison.OrdinalIgnoreCase) || String.Equals(plan.target, plan.BackupDirectory, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Резервная папка должна находиться вне приложения");
            plan.files = BundleAudit.ReadManifest(source).Files.Select(e => e.Path).Concat(new[] { "bundle-files.json", "installation.json" }).ToArray();
            HashSet<string> owned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try { foreach (BundleEntry entry in BundleAudit.ReadManifest(target).Files) owned.Add(entry.Path.Replace('\\', '/')); owned.Add("bundle-files.json"); owned.Add("installation.json"); }
            catch (Exception) { /* 1.0 did not record ownership; preserve all collisions. */ }
            List<string> unknown = new List<string>();
            foreach (string name in plan.files)
            {
                string destination = BundleAudit.SafePath(target, name);
                string incoming = BundleAudit.SafePath(source, name);
                string backup = BundleAudit.SafePath(plan.BackupDirectory, name);
                if (!File.Exists(incoming) || Directory.Exists(destination)) throw new InvalidDataException("Недопустимый файл установки: " + name);
                if (File.Exists(backup)) throw new InvalidDataException("Резервная папка уже используется");
                if (File.Exists(destination)) { plan.existed.Add(name); if (!owned.Contains(name.Replace('\\', '/'))) unknown.Add(name); }
            }
            // Complete backups before any application file is touched or process stopped.
            foreach (string name in plan.existed)
            {
                string backup = BundleAudit.SafePath(plan.BackupDirectory, name);
                Directory.CreateDirectory(Path.GetDirectoryName(backup));
                File.Copy(BundleAudit.SafePath(target, name), backup, false);
            }
            plan.UnknownCollisions = unknown.ToArray();
            return plan;
        }
        public void Apply() { Apply(null); }
        public void Apply(Action<string> beforeCopy)
        {
            List<string> touched = new List<string>();
            try
            {
                foreach (string name in files)
                {
                    if (beforeCopy != null) beforeCopy(name);
                    touched.Add(name);
                    ReplaceFile(BundleAudit.SafePath(source, name), BundleAudit.SafePath(target, name));
                }
                AuditResult audit = BundleAudit.ValidateDirectory(target, true);
                if (!audit.Success || !audit.IsSetup) throw new InvalidDataException("Не удалось проверить установленный комплект: " + String.Join("\n", audit.Missing));
            }
            catch (Exception error)
            {
                List<string> failures = new List<string>();
                foreach (string name in touched.AsEnumerable().Reverse())
                {
                    try
                    {
                        string destination = BundleAudit.SafePath(target, name);
                        if (existed.Contains(name)) ReplaceFile(BundleAudit.SafePath(BackupDirectory, name), destination);
                        else if (File.Exists(destination)) File.Delete(destination);
                    }
                    catch (Exception rollbackError) { failures.Add(name + ": " + rollbackError.Message); }
                }
                if (failures.Count != 0) throw new IOException("Не удалось восстановить часть файлов. Резервная копия: " + BackupDirectory + "\n" + String.Join("\n", failures), error);
                throw new IOException("Установка отменена; прежние файлы восстановлены. Резервная копия: " + BackupDirectory, error);
            }
        }
        private static void ReplaceFile(string source, string destination)
        {
            string parent = Path.GetDirectoryName(destination);
            Directory.CreateDirectory(parent);
            string temporary = BundleAudit.SafePath(parent, ".setup-" + Guid.NewGuid().ToString("N") + ".tmp");
            string displaced = BundleAudit.SafePath(parent, ".setup-old-" + Guid.NewGuid().ToString("N") + ".tmp");
            try
            {
                File.Copy(source, temporary, false);
                // Move directory entries rather than truncating a possible hard link.
                // File.Replace can fail on sandbox/volume metadata permissions.
                if (File.Exists(destination)) File.Move(destination, displaced);
                try { File.Move(temporary, destination); }
                catch { if (File.Exists(displaced) && !File.Exists(destination)) File.Move(displaced, destination); throw; }
                if (File.Exists(displaced)) File.Delete(displaced);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
    }
}
