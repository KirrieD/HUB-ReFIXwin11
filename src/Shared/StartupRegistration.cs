using System;
using Microsoft.Win32;
namespace ReFix.Shared
{
    public interface IStartupStore
    {
        bool? Preference { get; set; }
        string Command { get; }
        void WriteCommand(string command);
    }
    public static class StartupRegistration
    {
        public static void Ensure(IStartupStore store,string executable)
        {
            if(store.Preference==false)return;
            SetEnabled(store,executable,true);
        }
        public static void SetEnabled(IStartupStore store,string executable,bool enabled)
        {
            if(String.IsNullOrWhiteSpace(executable)||!System.IO.Path.IsPathRooted(executable)||executable.Contains("\""))throw new ArgumentException("Invalid launcher path");
            string command=enabled?"\""+executable+"\" --background":null;
            if(!String.Equals(store.Command,command,StringComparison.Ordinal))store.WriteCommand(command);
            if(store.Preference!=enabled)store.Preference=enabled;
        }
    }
    public sealed class RegistryStartupStore : IStartupStore
    {
        private const string RunKey=@"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string SettingsKey=@"Software\ReFIXHub";
        public bool? Preference
        {
            get { using(var key=Registry.CurrentUser.OpenSubKey(SettingsKey)){object value=key==null?null:key.GetValue("AutoStartEnabled");return value==null?(bool?)null:Convert.ToInt32(value)!=0;} }
            set { using(var key=Registry.CurrentUser.CreateSubKey(SettingsKey)){if(value.HasValue)key.SetValue("AutoStartEnabled",value.Value?1:0,RegistryValueKind.DWord);else key.DeleteValue("AutoStartEnabled",false);} }
        }
        public string Command { get { using(var key=Registry.CurrentUser.OpenSubKey(RunKey))return key==null?null:key.GetValue("ReFIXHub") as string; } }
        public void WriteCommand(string command) { using(var key=Registry.CurrentUser.CreateSubKey(RunKey)){if(command==null)key.DeleteValue("ReFIXHub",false);else key.SetValue("ReFIXHub",command,RegistryValueKind.String);} }
    }
}
