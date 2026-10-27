using System;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using ReFix.Shared;

internal static class ScopedThemeTests
{
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static void Until(Func<bool> condition){DateTime end=DateTime.UtcNow.AddSeconds(4);while(!condition()&&DateTime.UtcNow<end)Thread.Sleep(20);Check(condition(),"Live theme change did not arrive");}
    internal static void Run(Action<string,bool> report)
    {
        string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"refix-scoped-"+Guid.NewGuid().ToString("N"));
        try {
            using(var hub=new ThemeStore(root))
            using(var mixer=new ThemeStore(root,"quiet-mixer"))
            using(var menu=new ThemeStore(root,"task-menu")) {
                var p=hub.Current;p.Mode="Custom";p.Accent="#123456";hub.Save(p);
                Until(delegate{return mixer.Current.Accent=="#123456";});
                Check(!mixer.ScopeOverrideEnabled,"New extensions must inherit HUB");
                p=mixer.Current;p.Accent="#ABCDEF";mixer.Save(p);
                Check(mixer.Current.Accent=="#ABCDEF"&&hub.Current.Accent=="#123456","Scope edits must stay isolated");
                Until(delegate{return menu.Current.Accent=="#123456";});
                p=hub.Current;p.Mode="System";hub.Save(p);
                Until(delegate{return mixer.Current.Mode=="System";});
                Check(mixer.ScopedCurrent.Accent=="#ABCDEF","System must preserve scoped custom values");
                p=mixer.ScopedCurrent;p.GlowOpacity=.75;mixer.Save(p);
                Check(mixer.Current.Mode=="System"&&hub.Current.Mode=="System","Editing a scope must never switch global mode");
                p=hub.Current;p.Mode="Custom";hub.Save(p);
                Until(delegate{return mixer.Current.Accent=="#ABCDEF";});
                mixer.SetScopeOverrideEnabled(false);
                Check(mixer.Current.Accent=="#123456","Disabled scope must inherit");
                mixer.SetScopeOverrideEnabled(true);
                Check(mixer.Current.Accent=="#ABCDEF","Re-enabled scope must restore values");
                report("scoped themes inherit, isolate and preserve custom values in System",true);
            }
            using(var reader=new ThemeStore(root,"quiet-mixer")) {
                using(var writer=new ThemeStore(root,"quiet-mixer")) {
                    var scoped=writer.ScopedCurrent;scoped.Accent="#987654";writer.Save(scoped);
                    Until(delegate{return reader.Current.Accent=="#987654";});
                }
                var p=new ThemeProfile{Mode="Custom",Accent="#654321"};
                string file=Path.Combine(root,"theme.json"),temp=file+".replace";
                File.WriteAllText(temp,new JavaScriptSerializer().Serialize(p));File.Replace(temp,file,null);
                Until(delegate{return reader.GlobalCurrent.Accent=="#654321";});
                File.Delete(Path.Combine(root,"theme.quiet-mixer.json"));
                Until(delegate{return !reader.ScopeOverrideEnabled&&reader.Current.Accent=="#654321";});
                using(var locked=new FileStream(file,FileMode.Open,FileAccess.Write,FileShare.None)) {
                    byte[] data=System.Text.Encoding.UTF8.GetBytes(new JavaScriptSerializer().Serialize(new ThemeProfile{Mode="Custom",Accent="#102030"}));
                    locked.SetLength(0);locked.Write(data,0,data.Length);locked.Flush();Thread.Sleep(220);
                }
                Until(delegate{return reader.Current.Accent=="#102030";});
                File.Delete(file);Until(delegate{return reader.Current.Mode=="System";});
                File.WriteAllText(file,"{invalid");Thread.Sleep(850);
                var retries=typeof(ThemeStore).GetField("readRetries",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
                Check((int)retries.GetValue(reader)==5,"Malformed files must exhaust a bounded read retry budget");
                Thread.Sleep(200);Check((int)retries.GetValue(reader)==5,"No recurring read timer may reset the idle retry budget");
                report("scoped themes observe atomic replacement, temporary locks and deletion",true);
            }
        } finally {if(Directory.Exists(root))Directory.Delete(root,true);}
    }
}
