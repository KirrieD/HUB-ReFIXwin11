#if UPDATE_LAZY_FIXTURE
using System;
using System.IO;
using System.Linq;
using ReFix.Shared;
using ReFix.Hub;
namespace ReFix.Tests
{
    internal static class UpdateLazyTests
    {
        static bool Loaded(){return AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="PresentationFramework");}
        [STAThread]static int Main()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-lazy-"+Guid.NewGuid().ToString("N"));
            try
            {
                if(Loaded())throw new Exception("Fixture started with WPF loaded");
                using(var themes=new ThemeStore(root))using(var updater=new UpdateCoordinator(root,AppDomain.CurrentDomain.BaseDirectory,IntPtr.Zero,delegate{},delegate{},themes))
                    if(Loaded())throw new Exception("Background updater eagerly loads WPF");
                if(Loaded())throw new Exception("Background updater disposal eagerly loads WPF");
                Console.WriteLine("PASS themed updater preserves lazy WPF startup and disposal");return 0;
            }
            catch(Exception ex){Console.WriteLine(ex);return 1;}
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
#endif
