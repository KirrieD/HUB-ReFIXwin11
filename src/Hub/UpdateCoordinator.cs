using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReFix.Shared;
using System.Runtime.CompilerServices;

namespace ReFix.Hub
{
    internal interface IUpdatePresentation:IDisposable
    {
        bool Confirm(HubRelease release);void ShowProgress();void SetProgress(int percent,string text);void CloseProgress();void ShowFailure(string message);
    }
    internal sealed class UpdateCoordinator:IDisposable
    {
        internal const int Message=0x8104;
        private readonly HubUpdateService service;
        private readonly IntPtr window;
        private readonly Action<string> report;
        private readonly Action exit;
        private readonly string declinedPath;
        private readonly Queue<Action> pending=new Queue<Action>();
        private readonly object gate=new object();
        private readonly ThemeStore themes;
        private readonly Func<bool> hubVisible;
        private IUpdatePresentation presentation;
        private bool disposed,busy;
        private DateTime lastCheck=DateTime.MinValue;
        internal UpdateCoordinator(string root,string directory,IntPtr hwnd,Action<string> notice,Action quit,ThemeStore themes,Func<bool> hubVisible=null)
        {
            this.themes=themes;this.hubVisible=hubVisible;service=new HubUpdateService(root,directory);window=hwnd;report=notice;exit=quit;declinedPath=Path.Combine(root,"declined-update.txt");service.Progress+=Progress;
        }
        internal void Check(bool manual)
        {
            lock(gate){if(disposed||busy||!manual&&(DateTime.UtcNow-lastCheck).TotalHours<6)return;busy=true;lastCheck=DateTime.UtcNow;}
            if(manual)report("Проверка обновлений HUB…");
            Task.Run(async delegate
            {
                try
                {
                    HubRelease release=await service.CheckAsync();
                    Post(delegate
                    {
                        if(release==null){Finish();if(manual)report("Установлена актуальная версия HUB "+ProductInfo.Version);return;}
                        if(!manual&&ReadDeclined()==release.Version){Finish();return;}
                        if(!Confirm(release)){try{File.WriteAllText(declinedPath,release.Version);}catch(IOException){}catch(UnauthorizedAccessException){}Finish();report("Обновление отложено. Его можно установить через «Проверить обновления»." );return;}
                        Download(release);
                    });
                }
                catch(Exception ex){Program.Log(ex);string failure=HubUpdateService.FailureMessage(ex);Post(delegate{Finish();if(manual)report("Не удалось проверить обновление: "+failure);});}
            });
        }
        private IUpdatePresentation Presentation{get{if(presentation==null)presentation=CreatePresentation(themes);return presentation;}}
        [MethodImpl(MethodImplOptions.NoInlining)]
        private static IUpdatePresentation CreatePresentation(ThemeStore themes){return new UpdatePresentation(themes);}
        private bool Confirm(HubRelease release){return Presentation.Confirm(release);}
        private void Download(HubRelease release)
        {
            Presentation.ShowProgress();
            Task.Run(async delegate
            {
                try
                {
                    PreparedHubUpdate prepared=await service.PrepareAsync(release);
                    Post(delegate
                    {
                        try{service.StartApply(prepared,hubVisible==null?(bool?)null:!hubVisible());CloseProgress();report("Обновление устанавливается. HUB перезапускается…");exit();}
                        catch(Exception ex){CloseProgress();Finish();report("Обновление не установлено: "+HubUpdateService.FailureMessage(ex));}
                    });
                }
                catch(Exception ex){Program.Log(ex);string failure=HubUpdateService.FailureMessage(ex);Post(delegate{CloseProgress();Finish();report("Обновление не установлено: "+failure);Presentation.ShowFailure(failure);});}
            });
        }
        private void Progress(int percent,string text){Post(delegate{if(presentation!=null)presentation.SetProgress(percent,text);});}
        private string ReadDeclined(){try{return File.Exists(declinedPath)?File.ReadAllText(declinedPath).Trim():"";}catch(IOException){return "";}catch(UnauthorizedAccessException){return "";}}
        private void Finish(){lock(gate)busy=false;}
        private void Post(Action action){lock(gate){if(disposed)return;pending.Enqueue(action);}Native.PostMessage(window,Message,IntPtr.Zero,IntPtr.Zero);}
        internal void ProcessMessage()
        {
            while(true){Action action;lock(gate){if(disposed||pending.Count==0)return;action=pending.Dequeue();}action();}
        }
        private void CloseProgress(){if(presentation!=null)presentation.CloseProgress();}
        public void Dispose(){lock(gate){disposed=true;pending.Clear();}service.Progress-=Progress;CloseProgress();}
    }
}
