using System;
using ReFix.Shared;
namespace ReFix.Tests
{
    internal static class StartupTests
    {
        private sealed class MemoryStore : IStartupStore
        {
            public bool? Preference { get; set; }
            public string Command { get; set; }
            public int Writes;
            public void WriteCommand(string command) { Command=command; Writes++; }
        }
        public static void Run(Action<string,bool> report)
        {
            var store=new MemoryStore();
            string first=@"O:\Папка с пробелами\HUB-ReFIX.exe";
            StartupRegistration.Ensure(store,first);
            report("first portable launch enables current-user startup with quoted absolute path",store.Command=="\""+first+"\" --background" && store.Preference==true);
            int writes=store.Writes;StartupRegistration.Ensure(store,first);
            report("unchanged startup registration is not rewritten",store.Writes==writes);
            string moved=@"D:\Другой путь\Переименованный HUB.exe";
            StartupRegistration.Ensure(store,moved);
            report("moving and renaming launcher updates startup path",store.Command=="\""+moved+"\" --background");
            StartupRegistration.SetEnabled(store,moved,false);
            StartupRegistration.Ensure(store,first);
            report("explicitly disabled startup stays disabled after relaunch and move",store.Command==null && store.Preference==false);
            StartupRegistration.SetEnabled(store,first,true);
            report("startup can be enabled again",store.Preference==true && store.Command.Contains(first));
            var legacy=new MemoryStore { Command="old command" };
            StartupRegistration.Ensure(legacy,moved);
            report("existing installations refresh stale startup path",legacy.Command.Contains(moved));
        }
    }
}
