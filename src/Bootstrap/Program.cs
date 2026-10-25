using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using Microsoft.Win32;

namespace ReFix.Bootstrap
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                bool setup = Assembly.GetExecutingAssembly().GetManifestResourceNames().Contains("HubPayload.zip");
                if (args.Length > 0 && args[0] == "--extract-to")
                {
                    if (args.Length != 2) throw new ArgumentException("Использование: --extract-to <пустая папка>");
                    if (!setup) throw new InvalidOperationException("В этом файле нет архива установки");
                    using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("HubPayload.zip")) BundleAudit.ExtractPayload(payload, Path.GetFullPath(args[1]));
                    return 0;
                }
                Application.EnableVisualStyles(); Application.SetCompatibleTextRenderingDefault(false);
                if (setup) { Application.Run(new SetupWindow()); return 0; }
                string directory = AppDomain.CurrentDomain.BaseDirectory;
                bool exit = args.Contains("--exit");
                if (exit)
                {
                    foreach (string file in new[] { "HUB-ReFIX.Core.exe", "HUB-ReFIX.Core.exe.config", "ReFix.Shared.dll" }) if (!File.Exists(BundleAudit.SafePath(directory, file))) throw new FileNotFoundException(file);
                    if (!BundleAudit.HasRuntime()) throw new InvalidOperationException("Microsoft .NET Framework 4.8");
                }
                else
                {
                    AuditResult audit = BundleAudit.ValidateDirectory(directory);
                    if (!audit.Success) throw new InvalidDataException(String.Join("\n", audit.Missing));
                    args = args.Where(a => a != "--setup-verified" && a != "--portable-verified").ToArray();
                    args = args.Concat(new[] { audit.IsSetup ? "--setup-verified" : "--portable-verified" }).ToArray();
                }
                using (Process child = Process.Start(new ProcessStartInfo(Path.Combine(directory, "HUB-ReFIX.Core.exe"), String.Join(" ", args.Select(Quote))) { WorkingDirectory = directory, UseShellExecute = false }))
                    if (exit) { if (!child.WaitForExit(10000)) return 1; return child.ExitCode; }
                return 0;
            }
            catch (Exception ex)
            {
                string message = BundleAudit.RequiredMessage + "\n\n" + ex.Message;
                if (args.Length > 0 && args[0] == "--extract-to") Console.Error.WriteLine(message);
                else MessageBox.Show(message, "HUB ReFIX", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return 1;
            }
        }
        internal static string Quote(string value)
        {
            StringBuilder result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value) { if (c == '\\') { slashes++; continue; } if (c == '"') { result.Append('\\', slashes * 2 + 1).Append(c); slashes = 0; continue; } result.Append('\\', slashes).Append(c); slashes = 0; }
            return result.Append('\\', slashes * 2).Append('"').ToString();
        }
    }
    internal sealed class SetupWindow : Form
    {
        private readonly Button install = new Button { Text = "Установить", Width = 140, Height = 34, Left = 30, Top = 170 };
        private readonly Label status = new Label { Left = 30, Top = 25, Width = 500, Height = 135 };
        internal SetupWindow()
        {
            Text = "Установка HUB ReFIX"; ClientSize = new System.Drawing.Size(560, 230); FormBorderStyle = FormBorderStyle.FixedDialog; MaximizeBox = false; StartPosition = FormStartPosition.CenterScreen;
            status.Text = "HUB ReFIX будет установлен для текущего пользователя.\n\nПапка: " + Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReFIXHub", "app") + "\n\nБудут добавлены ярлык и запуск при входе в Windows.";
            Controls.Add(status); Controls.Add(install);
            Button cancel = new Button { Text = "Отмена", Left = 390, Top = 170, Width = 140, Height = 34 }; cancel.Click += delegate { Close(); }; Controls.Add(cancel); CancelButton = cancel;
            install.Click += delegate
            {
                install.Enabled = cancel.Enabled = false; status.Text = "Проверка архива и установка…"; Update();
                try { SetupInstaller.Install(); status.Text = BundleAudit.InstalledMessage; Close(); }
                catch (Exception ex) { status.Text = BundleAudit.RequiredMessage; MessageBox.Show(this, BundleAudit.RequiredMessage + "\n\n" + ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning); install.Enabled = cancel.Enabled = true; }
            };
        }
    }
    internal static class SetupInstaller
    {
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        internal static void Install()
        {
            if (!String.IsNullOrEmpty(Environment.GetEnvironmentVariable("REFIX_DATA_ROOT"))) throw new InvalidOperationException("Очистите REFIX_DATA_ROOT перед установкой.");
            if (!BundleAudit.HasRuntime()) throw new InvalidOperationException("Требуется Microsoft .NET Framework 4.8");
            string parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ReFIXHub");
            string target = BundleAudit.SafePath(parent, "app");
            string stage = BundleAudit.SafePath(parent, "setup-" + Guid.NewGuid().ToString("N"));
            try
            {
                using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("HubPayload.zip")) BundleAudit.ExtractPayload(payload, stage);
                BundleManifest manifest = BundleAudit.ReadManifest(stage);
                // Preflight every destination before requesting the old process to exit.
                foreach (BundleEntry entry in manifest.Files) BundleAudit.SafePath(target, entry.Path);
                BundleAudit.SafePath(target, "bundle-files.json");
                string markerPath = BundleAudit.SafePath(target, "installation.json");
                if (File.Exists(markerPath))
                {
                    if (new FileInfo(markerPath).Length > 4096) throw new InvalidDataException("Неизвестный installation.json: файл сохранён");
                    InstallationMarker previous = BundleAudit.ReadJson<InstallationMarker>(markerPath);
                    if (previous == null || previous.Mode != "Setup" || previous.Version == null || !System.Text.RegularExpressions.Regex.IsMatch(previous.Version, @"^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$")) throw new InvalidDataException("Неизвестный installation.json: файл сохранён");
                }
                File.WriteAllText(BundleAudit.SafePath(stage, "installation.json"), new JavaScriptSerializer().Serialize(new InstallationMarker { Mode = "Setup", Version = manifest.Version }), new UTF8Encoding(false));
                string backup = BundleAudit.SafePath(parent, "install-backups/" + DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N"));
                InstallFileTransaction transaction = InstallFileTransaction.Prepare(stage, target, backup);
                StopInstalledHub(stage);
                transaction.Apply();
                AuditResult audit = BundleAudit.ValidateDirectory(target); if (!audit.Success) throw new InvalidDataException(String.Join("\n", audit.Missing));
                string exe = Path.Combine(target, "HUB-ReFIX.exe");
                using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKey))
                {
                    SaveLegacyMarker(parent, run);
                    run.SetValue("ReFIXHub", Program.Quote(exe) + " --background");
                }
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                dynamic shell = Activator.CreateInstance(shellType);
                try
                {
                    dynamic shortcut = shell.CreateShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "HUB ReFIX.lnk"));
                    try { shortcut.TargetPath = exe; shortcut.WorkingDirectory = target; shortcut.Description = "HUB ReFIX - Ctrl+Alt+H"; shortcut.Save(); }
                    finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut); }
                }
                finally { System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell); }
                if (transaction.UnknownCollisions.Length != 0) MessageBox.Show("Предыдущие файлы без записи о принадлежности сохранены в:\n" + transaction.BackupDirectory + "\n\n" + String.Join("\n", transaction.UnknownCollisions), "HUB ReFIX — резервная копия", MessageBoxButtons.OK, MessageBoxIcon.Information);
                Process.Start(new ProcessStartInfo(exe) { WorkingDirectory = target, UseShellExecute = false });
            }
            finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
        }
        private static void StopInstalledHub(string verifiedStage)
        {
            // Core's --exit only posts to the SID + data-root window. This also reaches
            // an old portable 1.0 process running from Downloads before the first install.
            Process[] running = new[] { "HUB-ReFIX", "HUB-ReFIX.Core" }.SelectMany(Process.GetProcessesByName).ToArray();
            try
            {
                using (Process request = Process.Start(new ProcessStartInfo(Path.Combine(verifiedStage, "HUB-ReFIX.Core.exe"), "--exit") { WorkingDirectory = verifiedStage, UseShellExecute = false, CreateNoWindow = true }))
                    if (!request.WaitForExit(5000) || request.ExitCode != 0) throw new IOException("Закройте HUB через меню в трее и повторите установку.");
                DateTime deadline = DateTime.UtcNow.AddSeconds(15);
                foreach (Process process in running)
                {
                    try
                    {
                        if (process.HasExited || process.SessionId != Process.GetCurrentProcess().SessionId) continue;
                        int remaining = Math.Max(0, (int)(deadline - DateTime.UtcNow).TotalMilliseconds);
                        if (!process.WaitForExit(remaining)) throw new IOException("Закройте HUB через меню в трее и повторите установку.");
                    }
                    catch (InvalidOperationException) { }
                }
            }
            finally { foreach (Process process in running) process.Dispose(); }
        }
        private static void SaveLegacyMarker(string parent, RegistryKey run)
        {
            string legacy = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"QuietMixer\QuietMixer.exe"));
            string value = run.GetValue("QuietMixer") as string;
            if (!File.Exists(legacy) || !(new[] { "\"" + legacy + "\" --background", "\"" + legacy + "\"", legacy }).Contains(value, StringComparer.Ordinal)) return;
            string marker = BundleAudit.SafePath(parent, "migration-quiet-mixer.json"); if (File.Exists(marker)) return;
            bool running = false;
            foreach (Process process in Process.GetProcessesByName("QuietMixer")) using (process) { try { if (String.Equals(process.MainModule.FileName, legacy, StringComparison.OrdinalIgnoreCase)) running = true; } catch (Exception) { } }
            File.WriteAllText(marker, new JavaScriptSerializer().Serialize(new { LegacyPath = legacy, RunValue = value, WasRunning = running }), new UTF8Encoding(false));
        }
    }
}
