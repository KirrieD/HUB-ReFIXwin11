#if UPDATE_HOSTING_FIXTURE
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using ReFix.Shared;
using ReFix.Hub;
using Forms=System.Windows.Forms;
namespace ReFix.Tests
{
    internal static class UpdateHostingTests
    {
        [STAThread]static int Main()
        {
            string root=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"update-host-"+Guid.NewGuid().ToString("N"));string previous=Environment.GetEnvironmentVariable("REFIX_DATA_ROOT");Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",root);
            Exception error=null;bool complete=false;UpdateWindow prompt=null;UpdateWindow progress=null;string stage="initializing";var elapsed=System.Diagnostics.Stopwatch.StartNew();HubSession session=null;
            try
            {
                Directory.CreateDirectory(root);string catalog=Path.Combine(root,"catalog.json");File.WriteAllText(catalog,"{\"SchemaVersion\":1,\"Modules\":[]}");
                using(var themes=new ThemeStore(root))using(var controls=new ControlsStore(root))using(var modules=new ModuleManager(root,catalog))
                using(var start=new Forms.Timer{Interval=50})using(var finish=new Forms.Timer{Interval=300})using(var timeout=new Forms.Timer{Interval=30000})
                {
                    Action fail=delegate{if(prompt!=null)prompt.Close();if(progress!=null)progress.Finish();Forms.Application.ExitThread();};
                    start.Tick+=delegate
                    {
                        start.Stop();try
                        {
                            if(Application.Current!=null)throw new Exception("Unexpected initial WPF Application");
                            stage="creating confirmation";Console.WriteLine("HOSTING "+stage+" "+elapsed.ElapsedMilliseconds+"ms");var confirm=new UpdateWindow(themes,UpdateWindowKind.Confirm,new HubRelease{Version="2.0.0",Notes="Hosting fixture"});
                            prompt=confirm;stage="closing confirmation";Console.WriteLine("HOSTING "+stage+" "+elapsed.ElapsedMilliseconds+"ms");Dispatcher.CurrentDispatcher.BeginInvoke(new Action(delegate{confirm.Close();}));confirm.ShowDialog();prompt=null;if(confirm.Accepted)throw new Exception("Closing prompt accepted an update");
                            stage="showing progress";Console.WriteLine("HOSTING "+stage+" "+elapsed.ElapsedMilliseconds+"ms");progress=new UpdateWindow(themes,UpdateWindowKind.Progress);progress.Show();progress.SetProgress(50,"Fixture progress");var p=themes.Current;p.Mode="Custom";p.Accent="#2345A1";themes.Save(p);stage="waiting for live theme";finish.Start();
                        }
                        catch(Exception ex){error=ex;fail();}
                    };
                    finish.Tick+=delegate
                    {
                        finish.Stop();try
                        {
                            if(((SolidColorBrush)progress.Resources["Hub.Accent"]).Color!=Color.FromRgb(35,69,161))throw new Exception("WinForms pump did not process live WPF theme change");
                            progress.Finish();progress=null;if(Application.Current!=null)throw new Exception("Update prompt created WPF Application singleton");
                            stage="opening first HUB";Console.WriteLine("HOSTING "+stage+" "+elapsed.ElapsedMilliseconds+"ms");session=new HubSession(themes,modules,controls,delegate{});session.Show();if(!session.IsVisible)throw new Exception("First HUB opening failed after update UI");complete=true;stage="closing first HUB";Console.WriteLine("HOSTING "+stage+" "+elapsed.ElapsedMilliseconds+"ms");session.Close();session=null;Forms.Application.ExitThread();
                        }
                        catch(Exception ex){error=ex;fail();}
                    };
                    timeout.Tick+=delegate{error=new TimeoutException("Update hosting fixture timed out at "+stage+" after "+elapsed.ElapsedMilliseconds+"ms");timeout.Stop();fail();};start.Start();timeout.Start();Forms.Application.Run();
                }
                if(error!=null)throw error;if(!complete)throw new Exception("Hosting sequence incomplete");Console.WriteLine("PASS WinForms tray hosts themed update UI, live progress and subsequent first HUB opening");return 0;
            }
            catch(Exception ex){Console.WriteLine(ex);return 1;}
            finally{if(session!=null)session.Close();Environment.SetEnvironmentVariable("REFIX_DATA_ROOT",previous);if(Directory.Exists(root))Directory.Delete(root,true);}
        }
    }
}
#endif
