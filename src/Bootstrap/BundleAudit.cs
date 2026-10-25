using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace ReFix.Bootstrap
{
    public sealed class AuditResult
    {
        public bool Success { get; set; }
        public string[] Missing { get; set; }
        public bool IsSetup { get; set; }
        public string Message { get; set; }
    }
    public sealed class BundleEntry { public string Path { get; set; } public string Sha256 { get; set; } }
    public sealed class BundleManifest { public int SchemaVersion { get; set; } public string Version { get; set; } public BundleEntry[] Files { get; set; } }
    public sealed class InstallationMarker { public string Mode { get; set; } public string Version { get; set; } }
    public static class BundleAudit
    {
        public const string RequiredMessage = "Требуется настройка для корректной работы";
        public const string InstalledMessage = "Все установлено";
        private static readonly string[] Required = { "HUB-ReFIX.exe", "HUB-ReFIX.exe.config", "HUB-ReFIX.Core.exe", "HUB-ReFIX.Core.exe.config", "ReFix.Shared.dll", "ReFix.UpdateHelper.exe", "catalog.json", "README.md", "tools/Install.ps1", "tools/Install.cmd", "tools/Uninstall.ps1", "tools/Uninstall.cmd" };
        public static bool HasRuntime()
        {
            foreach (RegistryView view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
                try { using (RegistryKey root = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view)) using (RegistryKey key = root.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full")) { if (key != null && Convert.ToInt32(key.GetValue("Release", 0)) >= 528040) return true; } } catch (Exception) { }
            return false;
        }
        public static string SafePath(string root, string relative)
        {
            if (String.IsNullOrEmpty(relative) || relative.Length > 200 || relative.Contains(":")) throw new InvalidDataException("Недопустимый путь: " + relative);
            string[] parts = relative.Replace('\\', '/').Split('/');
            foreach (string part in parts)
                if (part.Length == 0 || part == "." || part == ".." || part.EndsWith(".") || part.EndsWith(" ") || part.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Regex.IsMatch(part, @"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(\.|$)", RegexOptions.IgnoreCase)) throw new InvalidDataException("Недопустимый путь: " + relative);
            string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string full = Path.GetFullPath(Path.Combine(fullRoot, String.Join(Path.DirectorySeparatorChar.ToString(), parts)));
            if (!full.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Путь вне каталога");
            string check = full;
            while (check != null) { if ((File.Exists(check) || Directory.Exists(check)) && (File.GetAttributes(check) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Ссылка в пути: " + check); check = Path.GetDirectoryName(check); }
            return full;
        }
        public static T ReadJson<T>(string path)
        {
            if (new FileInfo(path).Length > 1024 * 1024) throw new InvalidDataException("Слишком большой JSON");
            return new JavaScriptSerializer { MaxJsonLength = 1024 * 1024, RecursionLimit = 16 }.Deserialize<T>(File.ReadAllText(path));
        }
        public static BundleManifest ReadManifest(string root)
        {
            BundleManifest manifest = ReadJson<BundleManifest>(SafePath(root, "bundle-files.json"));
            if (manifest == null || manifest.SchemaVersion != 1 || manifest.Version == null || !Regex.IsMatch(manifest.Version, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$") || manifest.Files == null || manifest.Files.Length > 256) throw new InvalidDataException("Некорректный bundle-files.json");
            HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (BundleEntry entry in manifest.Files)
            {
                if (entry == null || entry.Path == null || entry.Sha256 == null || !Regex.IsMatch(entry.Sha256, "^[0-9a-f]{64}$")) throw new InvalidDataException("Некорректная запись bundle-files.json");
                SafePath(root, entry.Path);
                string name = entry.Path.Replace('\\', '/');
                if (!names.Add(name) || (!Required.Contains(name) && name != "HUB-ReFIX.exe.config" && !Regex.IsMatch(name, @"^packages/(quiet-mixer|quick-note)-" + Regex.Escape(manifest.Version) + @"\.zip$"))) throw new InvalidDataException("Неизвестный или повторный файл: " + name);
            }
            foreach (string required in Required.Concat(new[] { "packages/quiet-mixer-" + manifest.Version + ".zip", "packages/quick-note-" + manifest.Version + ".zip" })) if (!names.Contains(required)) throw new InvalidDataException("Нет записи: " + required);
            return manifest;
        }
        public static AuditResult ValidateDirectory(string root) { return ValidateDirectory(root, HasRuntime()); }
        public static AuditResult ValidateDirectory(string root, bool runtimeAvailable)
        {
            List<string> missing = new List<string>(); bool setup = false;
            if (!runtimeAvailable) missing.Add("Microsoft .NET Framework 4.8");
            try
            {
                BundleManifest manifest = ReadManifest(root);
                foreach (BundleEntry entry in manifest.Files)
                {
                    string file = SafePath(root, entry.Path);
                    if (!File.Exists(file)) { missing.Add(entry.Path); continue; }
                    using (SHA256 sha = SHA256.Create()) using (Stream stream = File.OpenRead(file))
                        if (BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant() != entry.Sha256) missing.Add(entry.Path + " (контрольная сумма)");
                }
                string marker = SafePath(root, "installation.json");
                if (File.Exists(marker)) { InstallationMarker data = ReadJson<InstallationMarker>(marker); setup = data != null && data.Mode == "Setup" && data.Version == manifest.Version; }
            }
            catch (Exception ex) { missing.Add(ex.Message); }
            return new AuditResult { Success = missing.Count == 0, Missing = missing.ToArray(), IsSetup = setup && missing.Count == 0, Message = missing.Count != 0 ? RequiredMessage : setup ? InstalledMessage : "Архив проверен" };
        }
        public static string ExtractPayload(Stream stream, string destination)
        {
            if (Directory.Exists(destination) && Directory.EnumerateFileSystemEntries(destination).Any()) throw new InvalidDataException("Каталог распаковки должен быть пустым");
            Directory.CreateDirectory(destination); SafePath(destination, "bundle-files.json");
            using (ZipArchive archive = new ZipArchive(stream, ZipArchiveMode.Read, false))
            {
                if (archive.Entries.Count == 0 || archive.Entries.Count > 300) throw new InvalidDataException("Некорректный ZIP");
                long total = 0; HashSet<string> paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (ZipArchiveEntry entry in archive.Entries)
                {
                    string name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith("HUB-ReFIXwin11/", StringComparison.Ordinal)) throw new InvalidDataException("Некорректный корень ZIP");
                    string relative = name.Substring("HUB-ReFIXwin11/".Length);
                    if (relative.Length == 0 && name.EndsWith("/")) continue;
                    bool directory = relative.EndsWith("/"); if (directory) relative = relative.TrimEnd('/');
                    string target = SafePath(destination, relative);
                    if (!paths.Add(relative) || ((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000) throw new InvalidDataException("Повторный файл или ссылка ZIP");
                    total += entry.Length; if (entry.Length > 256L * 1024 * 1024 || total > 512L * 1024 * 1024) throw new InvalidDataException("Слишком большой ZIP");
                    if (directory) { Directory.CreateDirectory(target); continue; }
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    using (Stream input = entry.Open()) using (Stream output = new FileStream(target, FileMode.CreateNew)) { byte[] buffer = new byte[81920]; long written = 0; int count; while ((count = input.Read(buffer, 0, buffer.Length)) > 0) { written += count; if (written > entry.Length) throw new InvalidDataException("Размер ZIP не совпадает"); output.Write(buffer, 0, count); } if (written != entry.Length) throw new InvalidDataException("Неполный ZIP"); }
                }
            }
            AuditResult result = ValidateDirectory(destination, true);
            if (!result.Success) throw new InvalidDataException(result.Message + "\n" + String.Join("\n", result.Missing));
            return destination;
        }
    }
}
