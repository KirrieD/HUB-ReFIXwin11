#if MINI_ICONS_EXTERNAL_FIXTURE
using System;
using ReFix.MiniIcons;
namespace ReFix.Tests
{
    internal static class MiniIconsExternalTests
    {
        private static void Check(bool value, string detail) { if (!value) throw new Exception(detail); }
        [STAThread]
        public static int Main(string[] args)
        {
            if (args.Length == 1 && args[0] == "--probe")
            {
                using (ExternalWindowsOverflow overflow = new ExternalWindowsOverflow())
                {
                    bool available = overflow.RefreshAsync().GetAwaiter().GetResult();
                    Console.WriteLine("CanOpenWindowsOverflow=" + available + "; " + overflow.Detail);
                }
                return 0;
            }
int invoked = 0;
            using (ExternalWindowsOverflow native = new ExternalWindowsOverflow(delegate(bool invoke, Func<bool> permitted)
            {
                if (invoke && permitted()) invoked++;
                return true;
            }, 150))
            {
                Check(native.RefreshAsync().GetAwaiter().GetResult(), "Provider probe succeeds");
                Check(native.CanOpenWindowsOverflow && invoked == 0, "Read-only capability probe cannot invoke");
                Check(native.OpenWindowsOverflowAsync().GetAwaiter().GetResult() && invoked == 1, "Explicit user operation invokes provider");
            }
            using (ExternalWindowsOverflow slow = new ExternalWindowsOverflow(delegate(bool invoke, Func<bool> permitted)
            {
                System.Threading.Thread.Sleep(150);
                if (invoke && permitted()) invoked++;
                return true;
            }, 25))
            {
                Check(!slow.OpenWindowsOverflowAsync().GetAwaiter().GetResult(), "Slow provider returns within timeout");
                Check(!slow.RefreshAsync().GetAwaiter().GetResult(), "No second provider worker while pending");
                System.Threading.Thread.Sleep(180);
                Check(invoked == 1, "Lookup completing after timeout cannot invoke");
            }
            Console.WriteLine("Mini Icons external capability: 6 assertions passed; no desktop mutations.");
            return 0;
        }
    }
}
#endif