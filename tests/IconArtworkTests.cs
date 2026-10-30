using System;
using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using ReFix.Shared;
internal static class IconArtworkTests {
 static void Check(bool ok,string message){if(!ok)throw new Exception(message);}
 static Type Renderer { get { return typeof(ThemeProfile).Assembly.GetType("ReFix.Shared.ReFixIcons"); } }
 static Bitmap Draw(string id,int size,ThemeProfile p){return (Bitmap)Renderer.GetMethod("CreateBitmap").Invoke(null,new object[]{id,size,p});}
 static void Set(ThemeProfile p,string key,object value){typeof(ThemeProfile).GetProperty(key).SetValue(p,value,null);}
 static long Hash(Bitmap b){long h=17;unchecked{for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++)h=h*31+b.GetPixel(x,y).ToArgb();}return h;}
 static bool Contains(Bitmap b,Color color){for(int y=0;y<b.Height;y++)for(int x=0;x<b.Width;x++){Color c=b.GetPixel(x,y);if(c.A>220&&Math.Abs(c.R-color.R)<4&&Math.Abs(c.G-color.G)<4&&Math.Abs(c.B-color.B)<4)return true;}return false;}
 [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process,int flags);
 internal static void Run(Action<string,bool> report){
  Check(Renderer!=null,"Shared first-party icon renderer is missing");
  var p=new ThemeProfile();Check((string)typeof(ThemeProfile).GetProperty("IconFamily").GetValue(p,null)=="Color","Color must be the default");Check((bool)typeof(ThemeProfile).GetProperty("IconFollowTheme").GetValue(p,null),"Icons must follow custom theme by default");
  string[] ids={"hub","quiet-mixer","task-menu","quick-note","mini-icons","unknown"};
  var hashes=new System.Collections.Generic.HashSet<long>();foreach(string id in ids)using(var b=Draw(id,16,p)){Check(b.Width==16&&b.Height==16,"Requested size changed");Check(b.GetPixel(0,0).A==0,"Icons need transparent corners");Check(hashes.Add(Hash(b)),"Different apps need different glyphs");}
  report("icon artwork: six recognizable distinct transparent 16px glyphs",true);
  using(var b=Draw("hub",64,p))Check(Contains(b,Color.FromArgb(242,82,96)),"Stock HUB must be red");
  p.Mode="Custom";p.Accent="#12E456";p.Foreground="#E0D0C0";using(var b=Draw("quiet-mixer",64,p))Check(Contains(b,Color.FromArgb(18,228,86)),"Custom icons must use Accent");
  Set(p,"IconFollowTheme",false);using(var b=Draw("quiet-mixer",64,p))Check(Contains(b,Color.FromArgb(114,200,255)),"Follow toggle must restore mixer blue");
  Set(p,"IconFollowTheme",true);Set(p,"IconFamily","ReFIX");using(var b=Draw("hub",64,p)){Check(Contains(b,Color.FromArgb(18,228,86)),"ReFIX frame must use Accent");Check(Contains(b,Color.FromArgb(224,208,192)),"ReFIX glyph must use Foreground");}
  long refix;using(var b=Draw("task-menu",32,p))refix=Hash(b);Set(p,"IconFamily","Outline");using(var b=Draw("task-menu",32,p)){Check(Hash(b)!=refix,"Families must differ");Check(Contains(b,Color.FromArgb(224,208,192)),"Outline must use Foreground");}
    p.Mode="System";Set(p,"IconFamily","Color");using(var b=Draw("quiet-mixer",64,p))Check(Contains(b,Color.FromArgb(114,200,255)),"System must ignore custom tint colors");
  foreach(string family in new string[]{"Color","ReFIX","Outline"}){Set(p,"IconFamily",family);hashes.Clear();foreach(string id in ids)using(var b=Draw(id,16,p)){Check(hashes.Add(Hash(b)),"Each family must retain distinct app glyphs");long hash=Hash(b);using(var second=Draw(id,16,p))Check(Hash(second)==hash,"Idle artwork must be deterministic");}}
  report("icon artwork: palettes, families and custom theme switch",true);
  Set(p,"IconFamily","garbage");p.Normalize();Check((string)typeof(ThemeProfile).GetProperty("IconFamily").GetValue(p,null)=="Color","Invalid family must normalize");
  using(var b=Draw(null,-100,null))Check(b.Width==16,"Invalid sizes must safely clamp to 16");using(var b=Draw("unknown",Int32.MaxValue,null))Check(b.Width==256,"Large sizes must safely clamp to 256");
  p.Accent="invalid";p.Foreground=null;p.Mode="Custom";using(var b=Draw("hub",16,p))Check(b.Width==16,"Malformed profile must remain renderable");Check(p.Accent=="invalid","Renderer must not mutate supplied profile");
  int before=GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle,0);for(int i=0;i<150;i++)using(var icon=(Icon)Renderer.GetMethod("CreateIcon").Invoke(null,new object[]{"hub",16,p})){using(var b=icon.ToBitmap())Check(b.Width==16,"Returned native icon must remain alive");}int after=GetGuiResources(System.Diagnostics.Process.GetCurrentProcess().Handle,0);Check(after-before<10,"Native icon handles leak");
  report("icon artwork: malformed inputs, ownership and GDI lifetime",true);
  foreach(var assembly in AppDomain.CurrentDomain.GetAssemblies())Check(assembly.GetName().Name!="PresentationFramework","Icon rendering must not eagerly load WPF");report("icon artwork: rendering keeps WPF lazy",true);
 }
}
