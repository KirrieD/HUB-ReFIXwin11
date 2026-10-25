using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Web.Script.Serialization;
using ReFix.Bootstrap;

namespace ReFix.Tests
{
    internal static class BootstrapTests
    {
        internal static void Run(Action<string, bool> report)
        {
            string root = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "refix-bootstrap-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                MakeBundle(root);
                AuditResult audit = BundleAudit.ValidateDirectory(root, true);
                report("portable bootstrap audit succeeds without Shared load", audit.Success && !audit.IsSetup);
                audit = BundleAudit.ValidateDirectory(root, false);
                report("bootstrap reports missing runtime", !audit.Success && audit.Missing.Contains("Microsoft .NET Framework 4.8") && audit.Message == BundleAudit.RequiredMessage);
                foreach (string name in new[] { "HUB-ReFIX.Core.exe", "ReFix.Shared.dll", "catalog.json", "HUB-ReFIX.Core.exe.config", "packages/quiet-mixer-1.1.0.zip" })
                {
                    string path = BundleAudit.SafePath(root, name); byte[] bytes = File.ReadAllBytes(path); File.Delete(path);
                    audit = BundleAudit.ValidateDirectory(root, true);
                    report("bootstrap reports missing " + name, !audit.Success && audit.Missing.Any(p => p.Replace('\\', '/') == name));
                    File.WriteAllBytes(path, bytes);
                }
                string catalog = Path.Combine(root, "catalog.json"); File.AppendAllText(catalog, "tampered");
                report("bootstrap detects hash mismatch", !BundleAudit.ValidateDirectory(root, true).Success);
                MakeBundle(root);
                File.WriteAllText(Path.Combine(root, "installation.json"), "{\"Mode\":\"Setup\",\"Version\":\"1.1.0\"}");
                audit = BundleAudit.ValidateDirectory(root, true);
                report("Setup marker yields exact installed message", audit.Success && audit.IsSetup && audit.Message == "Все установлено");
                File.WriteAllText(Path.Combine(root, "installation.json"), "{\"Mode\":\"Setup\",\"Version\":\"1.0.0\"}");
                report("stale marker cannot claim installed current version", !BundleAudit.ValidateDirectory(root, true).IsSetup);
                File.Delete(Path.Combine(root, "bundle-files.json"));
                report("missing bundle manifest is reported", !BundleAudit.ValidateDirectory(root, true).Success);
                MakeBundle(root);
                string validExtract = Path.Combine(root, "valid-extracted");
                using (MemoryStream memory = new MemoryStream())
                {
                    using (ZipArchive zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                    {
                        foreach (string name in BundleAudit.ReadManifest(root).Files.Select(e => e.Path).Concat(new[] { "bundle-files.json" }))
                        {
                            ZipArchiveEntry entry = zip.CreateEntry("HUB-ReFIXwin11/" + name.Replace('\\', '/'));
                            using (Stream input = File.OpenRead(BundleAudit.SafePath(root, name))) using (Stream output = entry.Open()) input.CopyTo(output);
                        }
                    }
                    memory.Position = 0; BundleAudit.ExtractPayload(memory, validExtract);
                }
                report("Setup extracts and verifies complete portable ZIP", BundleAudit.ValidateDirectory(validExtract, true).Success);
                string manifest = Path.Combine(root, "bundle-files.json");
                File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("catalog.json", "../catalog.json"));
                report("manifest traversal rejected", !BundleAudit.ValidateDirectory(root, true).Success);
                MakeBundle(root);
                File.WriteAllText(manifest, File.ReadAllText(manifest).Replace("catalog.json", "CATALOG.JSON"));
                report("manifest unexpected path rejected", !BundleAudit.ValidateDirectory(root, true).Success);
                string extract = Path.Combine(root, "extracted"); bool rejected = false;
                using (MemoryStream memory = new MemoryStream())
                {
                    using (ZipArchive zip = new ZipArchive(memory, ZipArchiveMode.Create, true))
                    using (StreamWriter writer = new StreamWriter(zip.CreateEntry("HUB-ReFIXwin11/../escape.txt").Open())) writer.Write("escape");
                    memory.Position = 0;
                    try { BundleAudit.ExtractPayload(memory, extract); } catch (InvalidDataException) { rejected = true; }
                }
                report("Setup ZIP traversal rejected before write", rejected && !File.Exists(Path.Combine(root, "escape.txt")));
                rejected = false;
                try { BundleAudit.SafePath(root, "tools/NUL.txt"); } catch (InvalidDataException) { rejected = true; }
                report("reserved Windows filename rejected", rejected);
                File.WriteAllText(manifest, new String(' ', 1024 * 1024 + 1));
                report("oversized bootstrap manifest rejected", !BundleAudit.ValidateDirectory(root, true).Success);
                string installSource = Path.Combine(root, "install-source"); Directory.CreateDirectory(installSource); MakeBundle(installSource);
                File.WriteAllText(Path.Combine(installSource, "installation.json"), "{\"Mode\":\"Setup\",\"Version\":\"1.1.0\"}");
                string legacyTarget = Path.Combine(root, "legacy-app"); Directory.CreateDirectory(Path.Combine(legacyTarget, "packages"));
                File.WriteAllText(Path.Combine(legacyTarget, "HUB-ReFIX.exe"), "legacy monolithic bytes");
                string collisionName = "packages/quick-note-1.1.0.zip";
                string collisionFile = BundleAudit.SafePath(legacyTarget, collisionName); File.WriteAllText(collisionFile, "unknown user archive bytes");
                File.WriteAllText(Path.Combine(legacyTarget, "user-note.txt"), "keep notes");
                string backup = Path.Combine(root, "install-backup");
                InstallFileTransaction transaction = InstallFileTransaction.Prepare(installSource, legacyTarget, backup);
                report("legacy without manifest preserves unknown collisions before copying", transaction.UnknownCollisions.Any(p => p.Replace('\\', '/') == collisionName) && File.ReadAllText(BundleAudit.SafePath(backup, collisionName)) == "unknown user archive bytes" && File.ReadAllText(collisionFile) == "unknown user archive bytes");
                transaction.Apply();
                report("legacy installation upgrades while preserving unknown file bytes", BundleAudit.ValidateDirectory(legacyTarget, true).IsSetup && File.ReadAllText(Path.Combine(legacyTarget, "user-note.txt")) == "keep notes" && File.ReadAllText(BundleAudit.SafePath(backup, collisionName)) == "unknown user archive bytes");
                string rollbackTarget = Path.Combine(root, "rollback-app"); Directory.CreateDirectory(rollbackTarget);
                File.WriteAllText(Path.Combine(rollbackTarget, "HUB-ReFIX.exe"), "old executable");
                File.WriteAllText(Path.Combine(rollbackTarget, "installation.json"), "old marker bytes");
                transaction = InstallFileTransaction.Prepare(installSource, rollbackTarget, Path.Combine(root, "rollback-backup"));
                int copied = 0; bool failedCopy = false;
                try { transaction.Apply(delegate(string name) { if (++copied == 4) throw new IOException("injected copy failure"); }); } catch (IOException) { failedCopy = true; }
                report("partial Setup copy failure restores old files and removes new files", failedCopy && File.ReadAllText(Path.Combine(rollbackTarget, "HUB-ReFIX.exe")) == "old executable" && !File.Exists(Path.Combine(rollbackTarget, "HUB-ReFIX.exe.config")) && !File.Exists(Path.Combine(rollbackTarget, "HUB-ReFIX.Core.exe")) && File.ReadAllText(Path.Combine(rollbackTarget, "installation.json")) == "old marker bytes");
            }
            finally { Directory.Delete(root, true); }
        }
        private static void MakeBundle(string root)
        {
            string[] names = { "HUB-ReFIX.exe", "HUB-ReFIX.exe.config", "HUB-ReFIX.Core.exe", "HUB-ReFIX.Core.exe.config", "ReFix.Shared.dll", "ReFix.UpdateHelper.exe", "catalog.json", "README.md", "tools/Install.ps1", "tools/Install.cmd", "tools/Uninstall.ps1", "tools/Uninstall.cmd", "packages/quiet-mixer-1.1.0.zip", "packages/quick-note-1.1.0.zip" };
            BundleEntry[] entries = names.Select(delegate(string name)
            {
                string file = BundleAudit.SafePath(root, name); Directory.CreateDirectory(Path.GetDirectoryName(file)); File.WriteAllText(file, name, new UTF8Encoding(false));
                using (SHA256 sha = SHA256.Create()) return new BundleEntry { Path = name.Replace('/', '\\'), Sha256 = BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(file))).Replace("-", "").ToLowerInvariant() };
            }).ToArray();
            File.WriteAllText(Path.Combine(root, "bundle-files.json"), new JavaScriptSerializer().Serialize(new BundleManifest { SchemaVersion = 1, Version = "1.1.0", Files = entries }));
        }
    }
#if BOOTSTRAP_TESTS
    internal static class BootstrapTestRunner
    {
        private static int Main() { int failed = 0; BootstrapTests.Run(delegate(string name, bool ok) { Console.WriteLine((ok ? "PASS " : "FAIL ") + name); if (!ok) failed++; }); return failed == 0 ? 0 : 1; }
    }
#endif
}
