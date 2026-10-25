using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ReFix.Shared;
using Forms=System.Windows.Forms;

namespace ReFix.Hub
{
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
        private Forms.Form progressWindow;
        private Forms.ProgressBar bar;
        private Forms.Label status;
        private bool disposed,busy;
        private DateTime lastCheck=DateTime.MinValue;
        internal UpdateCoordinator(string root,string directory,IntPtr hwnd,Action<string> notice,Action quit)
        {
            service=new HubUpdateService(root,directory);window=hwnd;report=notice;exit=quit;declinedPath=Path.Combine(root,"declined-update.txt");service.Progress+=Progress;
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
                catch(Exception ex){Post(delegate{Finish();if(manual)report("Не удалось проверить обновление: "+ex.Message);else Program.Log(ex);});}
            });
        }
        private bool Confirm(HubRelease release)
        {
            using(Forms.Form dialog=new Forms.Form { Text="Обновление HUB ReFIX",Width=490,Height=290,FormBorderStyle=Forms.FormBorderStyle.FixedDialog,StartPosition=Forms.FormStartPosition.CenterScreen,MinimizeBox=false,MaximizeBox=false,Font=new System.Drawing.Font("Segoe UI",10) })
            {
                Forms.Label title=new Forms.Label { Text="Доступна новая версия "+release.Version,AutoSize=false,Left=22,Top=20,Width=430,Height=30,Font=new System.Drawing.Font("Segoe UI",13,System.Drawing.FontStyle.Bold) };
                Forms.Label text=new Forms.Label { Text="Сейчас установлена "+ProductInfo.Version+". После установки HUB перезапустится.\n\n"+(release.Notes??""),Left=22,Top=60,Width=430,Height=125,AutoEllipsis=true };
                Forms.Button accept=new Forms.Button { Text="Обновить",DialogResult=Forms.DialogResult.OK,Left=230,Top=205,Width=105,Height=32 };
                Forms.Button cancel=new Forms.Button { Text="Отмена",DialogResult=Forms.DialogResult.Cancel,Left=345,Top=205,Width=105,Height=32 };
                dialog.Controls.AddRange(new Forms.Control[]{title,text,accept,cancel});dialog.AcceptButton=accept;dialog.CancelButton=cancel;
                return dialog.ShowDialog()==Forms.DialogResult.OK;
            }
        }
        private void Download(HubRelease release)
        {
            progressWindow=new Forms.Form { Text="Обновление HUB ReFIX",Width=440,Height=170,FormBorderStyle=Forms.FormBorderStyle.FixedDialog,StartPosition=Forms.FormStartPosition.CenterScreen,ControlBox=false,Font=new System.Drawing.Font("Segoe UI",10) };
            status=new Forms.Label { Text="Скачивание и проверка обновления…",Left=20,Top=20,Width=385,Height=40 };
            bar=new Forms.ProgressBar { Left=20,Top=70,Width=385,Height=20,Style=Forms.ProgressBarStyle.Marquee };
            progressWindow.Controls.Add(status);progressWindow.Controls.Add(bar);progressWindow.Show();
            Task.Run(async delegate
            {
                try
                {
                    PreparedHubUpdate prepared=await service.PrepareAsync(release);
                    Post(delegate
                    {
                        try{service.StartApply(prepared);CloseProgress();report("Обновление устанавливается. HUB перезапускается…");exit();}
                        catch(Exception ex){CloseProgress();Finish();report("Обновление не установлено: "+ex.Message);}
                    });
                }
                catch(Exception ex){Post(delegate{CloseProgress();Finish();report("Обновление не установлено: "+ex.Message);Forms.MessageBox.Show("Текущая версия сохранена.\n\n"+ex.Message,"Обновление HUB",Forms.MessageBoxButtons.OK,Forms.MessageBoxIcon.Warning);});}
            });
        }
        private void Progress(int percent,string text){Post(delegate{if(progressWindow==null)return;status.Text=text;if(percent>=0){bar.Style=Forms.ProgressBarStyle.Continuous;bar.Value=Math.Max(0,Math.Min(100,percent));}});}
        private string ReadDeclined(){try{return File.Exists(declinedPath)?File.ReadAllText(declinedPath).Trim():"";}catch(IOException){return "";}catch(UnauthorizedAccessException){return "";}}
        private void Finish(){lock(gate)busy=false;}
        private void Post(Action action){lock(gate){if(disposed)return;pending.Enqueue(action);}Native.PostMessage(window,Message,IntPtr.Zero,IntPtr.Zero);}
        internal void ProcessMessage()
        {
            while(true){Action action;lock(gate){if(disposed||pending.Count==0)return;action=pending.Dequeue();}action();}
        }
        private void CloseProgress(){if(progressWindow!=null){progressWindow.Close();progressWindow.Dispose();progressWindow=null;}}
        public void Dispose(){lock(gate){disposed=true;pending.Clear();}service.Progress-=Progress;CloseProgress();}
    }
}
