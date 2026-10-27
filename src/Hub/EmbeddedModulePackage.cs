using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Web.Script.Serialization;
namespace ReFix.Hub
{
    // Keep the portable envelope compatible with already released updaters.
    // The new offline module travels inside the verified Core executable.
    internal sealed class EmbeddedModulePackage
    {
        private readonly byte[] bytes;
        internal ModuleManifest Manifest { get; private set; }
        private EmbeddedModulePackage(byte[] data,ModuleManifest manifest){bytes=data;Manifest=manifest;}
        internal static EmbeddedModulePackage Load()
        {
            using(Stream stream=Assembly.GetExecutingAssembly().GetManifestResourceStream("ReFix.TaskMenu.zip"))
            {
                if(stream==null)return null;
                if(stream.Length>PackageSafety.MaxArchive)throw new InvalidDataException("Embedded module is too large.");
                using(var buffer=new MemoryStream()){stream.CopyTo(buffer);return Read(buffer.ToArray());}
            }
        }
        internal static EmbeddedModulePackage Read(byte[] data)
        {
            if(data==null||data.Length==0||data.Length>PackageSafety.MaxArchive)throw new InvalidDataException("Invalid embedded module size.");
            ModuleManifest manifest;
            using(var zip=new ZipArchive(new MemoryStream(data,false),ZipArchiveMode.Read))
            {
                var entries=zip.Entries.Where(e=>e.FullName=="module.json").ToArray();
                if(entries.Length!=1||entries[0].Length>65536)throw new InvalidDataException("Invalid embedded module metadata.");
                using(var reader=new StreamReader(entries[0].Open()))manifest=new JavaScriptSerializer().Deserialize<ModuleManifest>(reader.ReadToEnd());
                if(manifest==null||manifest.Id!="task-menu"||manifest.Version!=ReFix.Shared.ProductInfo.Version||manifest.EntryPoint!="TaskMenu.exe"||manifest.Kind!="process"||!zip.Entries.Any(e=>e.FullName==manifest.EntryPoint))throw new InvalidDataException("Embedded module identity mismatch.");
            }
            using(var hash=SHA256.Create())manifest.Sha256=BitConverter.ToString(hash.ComputeHash(data)).Replace("-","").ToLowerInvariant();
            manifest.PackageUrl="";manifest.Validate();
            return new EmbeddedModulePackage((byte[])data.Clone(),manifest);
        }
        internal string Materialize(string root,ModuleManifest manifest)
        {
            if(manifest.Id!=Manifest.Id||manifest.Version!=Manifest.Version||!String.Equals(manifest.Sha256,Manifest.Sha256,StringComparison.OrdinalIgnoreCase))return null;
            string directory=Path.Combine(Path.GetFullPath(root),"embedded-packages");
            ReFix.Updating.UpdateEngine.NoLinks(directory);Directory.CreateDirectory(directory);
            string file=Path.Combine(directory,Manifest.Sha256+".zip");ReFix.Updating.UpdateEngine.NoLinks(file);
            if(File.Exists(file)&&PackageSafety.Hash(file)==Manifest.Sha256)return file;
            string temporary=file+"."+Guid.NewGuid().ToString("N")+".tmp";
            try{File.WriteAllBytes(temporary,bytes);if(File.Exists(file))File.Replace(temporary,file,null);else File.Move(temporary,file);}
            finally{if(File.Exists(temporary))File.Delete(temporary);}
            return file;
        }
    }
}
