using System;
using System.IO;
using System.Windows.Forms;
using ReFix.Updating;
namespace ReFix.UpdateHelper {
 internal static class Program {
  [STAThread] static int Main(string[] args){UpdatePlan plan=null;bool exited=false;try{if(args.Length!=2||args[0]!="--plan")throw new ArgumentException("Expected --plan <path>.");plan=UpdateEngine.LoadPlan(args[1]);if(!UpdateEngine.Under(plan.Work,System.Reflection.Assembly.GetExecutingAssembly().Location))throw new InvalidDataException("Helper must run from staging.");UpdateEngine.WaitForParent(plan);exited=true;UpdateEngine.Apply(plan,null);return 0;}catch(Exception e){if(plan!=null)try{File.WriteAllText(UpdateEngine.Inside(plan.Work,"update-error.txt"),e.ToString());}catch{}MessageBox.Show("Не удалось обновить HUB. Предыдущая версия восстановлена, если это было возможно.\n"+e.Message,"HUB-ReFIX",MessageBoxButtons.OK,MessageBoxIcon.Error);return 1;}finally{if(plan!=null&&exited)try{UpdateEngine.Restart(plan);}catch(Exception e){MessageBox.Show("Не удалось перезапустить HUB: "+e.Message,"HUB-ReFIX");}}}
 }
}
