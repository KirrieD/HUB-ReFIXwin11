using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using ReFix.Hub;
namespace ReFix.Tests
{
    internal static class EmbeddedModuleTests
    {
        internal static void Run(Action<string,bool> report)
        {
            byte[] bytes;
            using(var buffer=new MemoryStream())
            {
                using(var zip=new ZipArchive(buffer,ZipArchiveMode.Create,true))
                {
                    using(var writer=new StreamWriter(zip.CreateEntry("module.json").Open(),Encoding.UTF8))writer.Write("{\"Id\":\"task-menu\",\"Name\":\"Меню задач\",\"Version\":\""+ReFix.Shared.ProductInfo.Version+"\",\"Kind\":\"process\",\"EntryPoint\":\"TaskMenu.exe\",\"Preview\":\"menu\"}");
                    using(var writer=new StreamWriter(zip.CreateEntry("TaskMenu.exe").Open()))writer.Write("fixture");
                }
                bytes=buffer.ToArray();
            }
            var package=EmbeddedModulePackage.Read(bytes);
            report("embedded menu metadata derives SHA256 from exact binary resource",package.Manifest.Id=="task-menu" && package.Manifest.Sha256.Length==64 && package.Manifest.Preview=="menu");
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"embedded-test-"+Guid.NewGuid().ToString("N"));
            try
            {
                string cached=package.Materialize(root,package.Manifest);
                report("embedded package is available offline in user data",File.Exists(cached)&&PackageSafety.Hash(cached)==package.Manifest.Sha256);
                File.WriteAllText(cached,"broken cache");
                package.Materialize(root,package.Manifest);
                report("embedded package repairs modified cache before install",PackageSafety.Hash(cached)==package.Manifest.Sha256);
                var other=new ModuleManifest { Id="task-menu",Version="99.0.0",Sha256=package.Manifest.Sha256 };
                report("embedded package cannot shadow another module version",package.Materialize(root,other)==null);
            }
            finally { if(Directory.Exists(root))Directory.Delete(root,true); }
            bool rejected=false;try{EmbeddedModulePackage.Read(new byte[]{1,2,3});}catch(InvalidDataException){rejected=true;}
            report("invalid embedded ZIP rejected",rejected);
            string lifecycle=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"offline-menu-"+Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(lifecycle);
            try
            {
                string catalog=Path.Combine(lifecycle,"catalog.json");File.WriteAllText(catalog,"{\"SchemaVersion\":1,\"Modules\":[]}");
                var config=new ReFix.Shared.TaskMenuConfig();config.Panels.Add(new ReFix.Shared.TaskMenuPanel{Monitor="fixture-disconnected",Edge=ReFix.Shared.TaskMenuEdge.Top});new ReFix.Shared.TaskMenuStore(lifecycle).Save(config);
                using(var controls=new ReFix.Shared.ControlsStore(lifecycle))
                using(var manager=new ModuleManager(lifecycle,catalog))
                {
                    controls.Assign("task-menu.toggle","Ctrl+Alt+Shift+F19");
                    var view=manager.Modules[0];
                    manager.InstallLocalAsync(view).GetAwaiter().GetResult();
                    report("real menu installs offline from Core resource without catalogue or adjacent package",view.Manifest.Id=="task-menu"&&view.State=="OFF"&&File.Exists(Path.Combine(view.InstallDirectory,"TaskMenu.exe")));
                    try
                    {
                        manager.StartAsync(view).GetAwaiter().GetResult();
                        var runtime=controls.GetRuntime("task-menu.toggle");
                        report("real menu reports live pipe state and registered key",view.State=="ON"&&runtime!=null&&runtime.Actual=="Ctrl+Alt+Shift+F19"&&String.IsNullOrEmpty(runtime.Error));
                    }
                    finally {manager.UninstallAsync(view).GetAwaiter().GetResult();}
                    report("real menu uninstalls gracefully and preserves configuration",view.State=="NO"&&File.Exists(Path.Combine(lifecycle,"task-menu.json")));
                    controls.Assign("mini-icons.toggle","Ctrl+Alt+Shift+F20");
                    var mini=manager.Modules.Single(v=>v.Manifest.Id=="mini-icons");
                    manager.InstallLocalAsync(mini).GetAwaiter().GetResult();
                    report("real mini icons installs offline from exact embedded Core resource",mini.State=="OFF"&&File.Exists(Path.Combine(mini.InstallDirectory,"MiniIcons.exe")));
                    try
                    {
                        manager.StartAsync(mini).GetAwaiter().GetResult();
                        var runtime=controls.GetRuntime("mini-icons.toggle");
                        report("real mini icons reports active runtime and saved key",mini.State=="ON"&&runtime!=null&&runtime.Actual=="Ctrl+Alt+Shift+F20"&&String.IsNullOrEmpty(runtime.Error));
                        manager.ShowAsync(mini).GetAwaiter().GetResult();
                    }
                    finally {manager.UninstallAsync(mini).GetAwaiter().GetResult();}
                    report("real mini icons uninstalls and removes live tray host",mini.State=="NO"&&ReFix.Shared.MiniTrayFiles.ReadHost(lifecycle)==null);
                }
            }
            finally {if(Directory.Exists(lifecycle))Directory.Delete(lifecycle,true);}
        }
    }
}
