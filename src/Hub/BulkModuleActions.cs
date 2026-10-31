using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
namespace ReFix.Hub
{
    internal enum BulkOperation { Install, Update, Remove }
    internal sealed class BulkResult
    {
        internal int Completed,Skipped;
        internal readonly List<string> Errors=new List<string>();
    }
    internal static class BulkModuleActions
    {
        internal static bool Eligible(ModuleView view,BulkOperation operation)
        {
            if(view.IsBusy)return false;
            bool installed=!String.IsNullOrEmpty(view.InstalledVersion);
            if(operation==BulkOperation.Install)return !installed&&view.State=="NO";
            if(operation==BulkOperation.Remove)return installed;
            Version available,current;return installed&&Version.TryParse(view.Manifest.Version,out available)&&Version.TryParse(view.InstalledVersion,out current)&&available>current;
        }
        internal static bool? SelectionState(int selected,int total){return selected==0?false:selected==total?(bool?)true:null;}
        internal static async Task<BulkResult> RunAsync(IEnumerable<ModuleView> selected,BulkOperation operation,Func<ModuleView,Task> action,Action<ModuleView,int,int> progress=null)
        {
            var targets=selected.Distinct().ToArray();var result=new BulkResult();
            for(int i=0;i<targets.Length;i++)
            {
                var view=targets[i];if(!Eligible(view,operation)){result.Skipped++;continue;}
                if(progress!=null)progress(view,i+1,targets.Length);
                try{await action(view);result.Completed++;}catch(Exception ex){result.Errors.Add(view.Manifest.Name+": "+ex.Message);}
            }
            return result;
        }
    }
}
