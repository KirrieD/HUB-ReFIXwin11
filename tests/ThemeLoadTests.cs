using System;
using System.IO;
using System.Linq;
using ReFix.Shared;
namespace ReFix.Tests
{
    internal static class ThemeLoadTests
    {
        private static bool Loaded(){return AppDomain.CurrentDomain.GetAssemblies().Any(a=>a.GetName().Name=="PresentationFramework");}
        private static int Main()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"lazy-theme-"+Guid.NewGuid().ToString("N"));
            try
            {
                if(Loaded())throw new Exception("Fixture started with WPF already loaded");
                using(var theme=new ThemeStore(root))
                {
                    theme.Current.Normalize();
                    if(Loaded())throw new Exception("Background theme tracking eagerly loads WPF");
                }
                if(Loaded())throw new Exception("ThemeStore.Dispose eagerly loads WPF");
                Console.WriteLine("PASS theme tracking preserves lazy WPF startup and disposal");return 0;
            }
            catch(Exception ex){Console.WriteLine("FAIL "+ex);return 1;}
            finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
