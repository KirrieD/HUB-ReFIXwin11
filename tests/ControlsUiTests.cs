using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReFix.Shared;
using ReFix.Hub;
namespace ReFix.Tests {
public static class ControlsUiTests {
public static void Run(Action<string,bool> report) {
string root=Path.Combine(Path.GetTempPath(),"refix-controls-ui-"+Guid.NewGuid().ToString("N"));
try {using(var themes=new ThemeStore(root))using(var controls=new ControlsStore(root)) {
var window=new ControlsWindow(themes,controls);
try {
var flags=BindingFlags.Instance|BindingFlags.NonPublic;
var keys=(Dictionary<string,Border>)typeof(ControlsWindow).GetField("keys",flags).GetValue(window);
Color surface=((SolidColorBrush)window.Resources["Hub.Surface"]).Color;
report("assigned physical keys are filled before selection",((SolidColorBrush)keys["H"].Background).Color!=surface&&((SolidColorBrush)keys["Esc"].Background).Color!=surface);
report("keyboard shows every supported navigation key",keys.ContainsKey("Tab")&&keys.ContainsKey("Backspace")&&keys.ContainsKey("Delete")&&keys.ContainsKey("Insert")&&keys.ContainsKey("Home")&&keys.ContainsKey("End")&&keys.ContainsKey("PageUp")&&keys.ContainsKey("PageDown")&&keys.ContainsKey("F22")&&keys.ContainsKey("F24"));
typeof(ControlsWindow).GetField("selected",flags).SetValue(window,"hub.toggle");
typeof(ControlsWindow).GetMethod("Refresh",flags).Invoke(window,null);
report("selected chord fill differs from other assigned keys",((SolidColorBrush)keys["H"].Background).Color!=((SolidColorBrush)keys["S"].Background).Color&&((SolidColorBrush)keys["H"].Background).Color!=surface);
controls.Assign("hub.hide","Ctrl+PageDown");
typeof(ControlsWindow).GetField("selected",flags).SetValue(window,null);
typeof(ControlsWindow).GetMethod("Refresh",flags).Invoke(window,null);
report("assigned navigation key is visibly filled",((SolidColorBrush)keys["PageDown"].Background).Color!=surface);
typeof(ControlsWindow).GetField("selected",flags).SetValue(window,"hub.hide");
typeof(ControlsWindow).GetMethod("Refresh",flags).Invoke(window,null);
report("selected navigation chord is brightly filled",((SolidColorBrush)keys["PageDown"].Background).Color==((SolidColorBrush)window.Resources["Hub.Accent"]).Color);
window.Show();window.Height=window.MinHeight;window.UpdateLayout();
var inputs=(Dictionary<string,TextBox>)typeof(ControlsWindow).GetField("typedAssignments",flags).GetValue(window);
var list=Ancestor<ScrollViewer>(inputs["hub.hide"]);
bool visibleBefore=KeyboardVisible(window,keys);
Point before=keys["H"].TranslatePoint(new Point(),window);
if(list!=null){list.ScrollToEnd();Pump();window.UpdateLayout();}
Point after=keys["H"].TranslatePoint(new Point(),window);
report("keyboard stays fixed beside independently scrolling bindings",list!=null&&list.VerticalOffset>0&&Ancestor<ScrollViewer>(keys["H"])==null&&Math.Abs(before.X-after.X)<.1&&Math.Abs(before.Y-after.Y)<.1);
report("all keyboard rows remain visible at minimum window height",visibleBefore&&KeyboardVisible(window,keys));
}finally{window.Close();}
}}finally{if(Directory.Exists(root))Directory.Delete(root,true);}
}
static T Ancestor<T>(DependencyObject value)where T:DependencyObject{for(var parent=VisualTreeHelper.GetParent(value);parent!=null;parent=VisualTreeHelper.GetParent(parent)){var match=parent as T;if(match!=null)return match;}return null;}
static bool KeyboardVisible(Window window,Dictionary<string,Border> keys){foreach(var cap in keys.Values){Point first=cap.TranslatePoint(new Point(),window),last=cap.TranslatePoint(new Point(cap.ActualWidth,cap.ActualHeight),window);if(!cap.IsVisible||first.X<0||first.Y<0||last.X>window.ActualWidth+.1||last.Y>window.ActualHeight+.1)return false;}return true;}
static void Pump(){var frame=new System.Windows.Threading.DispatcherFrame();System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.ApplicationIdle,new Action(delegate{frame.Continue=false;}));System.Windows.Threading.Dispatcher.PushFrame(frame);}
#if CONTROLS_UI_STANDALONE
[STAThread] public static int Main(){int failed=0;Run(delegate(string n,bool ok){Console.WriteLine((ok?"PASS ":"FAIL ")+n);if(!ok)failed++;});return failed==0?0:1;}
#endif
}}
