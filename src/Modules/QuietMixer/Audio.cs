using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace QuietMixer
{
    internal sealed class AudioChannel : IDisposable
    {
        internal string Id, Name;
        internal uint ProcessId;
        private object native;
        private IAudioEndpointVolume endpoint;
        private ISimpleAudioVolume session;
        internal AudioChannel(string id, string name, uint pid, object value, bool master)
        {
            Id = id; Name = name; ProcessId = pid; native = value;
            if (master) endpoint = (IAudioEndpointVolume)value; else session = (ISimpleAudioVolume)value;
        }
        internal float Volume
        {
            get { float v; if (endpoint != null) endpoint.GetMasterVolumeLevelScalar(out v); else session.GetMasterVolume(out v); return v; }
            set { Guid g = Guid.Empty; if (endpoint != null) endpoint.SetMasterVolumeLevelScalar(value, ref g); else session.SetMasterVolume(value, ref g); }
        }
        internal bool Muted
        {
            get { bool v; if (endpoint != null) endpoint.GetMute(out v); else session.GetMute(out v); return v; }
            set { Guid g = Guid.Empty; if (endpoint != null) endpoint.SetMute(value, ref g); else session.SetMute(value, ref g); }
        }
        public void Dispose() { endpoint = null; session = null; AudioEngine.Release(native); native = null; }
    }

    internal sealed class AudioEngine : IDisposable
    {
        internal AudioChannel Master;
        internal string DeviceId;
        private IMMDevice device;
        internal static void Release(object o) { if (o != null && Marshal.IsComObject(o)) Marshal.ReleaseComObject(o); }
        internal AudioEngine()
        {
            IMMDeviceEnumerator enumerator = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                enumerator.GetDefaultAudioEndpoint(0, 1, out device);
                device.GetId(out DeviceId);
                Guid id = typeof(IAudioEndpointVolume).GUID; object volume;
                device.Activate(ref id, 23, IntPtr.Zero, out volume);
                Master = new AudioChannel("master", "Общая громкость", 0, volume, true);
            }
            catch { Dispose(); throw; }
            finally { Release(enumerator); }
        }
        internal bool IsCurrentDevice()
        {
            IMMDeviceEnumerator enumerator = null; IMMDevice current = null;
            try
            {
                enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
                enumerator.GetDefaultAudioEndpoint(0, 1, out current);
                string id; current.GetId(out id); return id == DeviceId;
            }
            catch (COMException) { return false; }
            finally { Release(current); Release(enumerator); }
        }
        internal List<AudioChannel> Sessions()
        {
            object managerObject = null; IAudioSessionEnumerator sessions = null;
            List<AudioChannel> result = new List<AudioChannel>();
            try
            {
                Guid id = typeof(IAudioSessionManager2).GUID;
                device.Activate(ref id, 23, IntPtr.Zero, out managerObject);
                ((IAudioSessionManager2)managerObject).GetSessionEnumerator(out sessions);
                int count; sessions.GetCount(out count);
                for (int i = 0; i < count; i++)
                {
                    IAudioSessionControl raw = null;
                    try
                    {
                        sessions.GetSession(i, out raw);
                        IAudioSessionControl2 control = (IAudioSessionControl2)raw;
                        int state; control.GetState(out state);
                        if (state == 2) continue; // Expired; inactive sessions remain adjustable.
                        string key, display; uint pid;
                        control.GetSessionInstanceIdentifier(out key); control.GetDisplayName(out display); control.GetProcessId(out pid);
                        if (control.IsSystemSoundsSession() == 0) display = "Системные звуки";
                        else if (string.IsNullOrWhiteSpace(display) || display.StartsWith("@"))
                        {
                            try { using (Process p = Process.GetProcessById((int)pid)) display = p.ProcessName; }
                            catch { display = "Приложение " + pid; }
                        }
                        result.Add(new AudioChannel(key, display, pid, control, false));
                        raw = null; // Ownership transferred to the row.
                    }
                    catch (COMException) { }
                    finally { Release(raw); }
                }
                result.Sort(delegate(AudioChannel a, AudioChannel b) { return StringComparer.CurrentCultureIgnoreCase.Compare(a.Name, b.Name); });
                return result;
            }
            catch { foreach (AudioChannel row in result) row.Dispose(); throw; }
            finally { Release(sessions); Release(managerObject); }
        }
        public void Dispose()
        {
            if (Master != null) { Master.Dispose(); Master = null; }
            Release(device); device = null;
        }
    }

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class MMDeviceEnumerator { }
    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDeviceEnumerator
    {
        void EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
        void GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
        void GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
    }
    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IMMDevice
    {
        void Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        void OpenPropertyStore(uint access, out IntPtr store);
        void GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetState(out uint state);
    }
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioEndpointVolume
    {
        void RegisterControlChangeNotify(IntPtr notify); void UnregisterControlChangeNotify(IntPtr notify);
        void GetChannelCount(out uint count);
        void SetMasterVolumeLevel(float level, ref Guid context);
        void SetMasterVolumeLevelScalar(float level, ref Guid context);
        void GetMasterVolumeLevel(out float level); void GetMasterVolumeLevelScalar(out float level);
        void SetChannelVolumeLevel(uint channel, float level, ref Guid context);
        void SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        void GetChannelVolumeLevel(uint channel, out float level); void GetChannelVolumeLevelScalar(uint channel, out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionManager2
    {
        void GetAudioSessionControl(ref Guid id, uint flags, out IntPtr control);
        void GetSimpleAudioVolume(ref Guid id, uint flags, out IntPtr volume);
        void GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }
    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionEnumerator
    {
        void GetCount(out int count);
        void GetSession(int index, out IAudioSessionControl control);
    }
    [ComImport, Guid("F4B1A599-7266-4319-A8CA-E70ACB11E8CD"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl
    {
        void GetState(out int state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        void GetGroupingParam(out Guid grouping); void SetGroupingParam(ref Guid grouping, ref Guid context);
        void RegisterAudioSessionNotification(IntPtr client); void UnregisterAudioSessionNotification(IntPtr client);
    }
    [ComImport, Guid("BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IAudioSessionControl2
    {
        void GetState(out int state);
        void GetDisplayName([MarshalAs(UnmanagedType.LPWStr)] out string name);
        void SetDisplayName([MarshalAs(UnmanagedType.LPWStr)] string name, ref Guid context);
        void GetIconPath([MarshalAs(UnmanagedType.LPWStr)] out string path);
        void SetIconPath([MarshalAs(UnmanagedType.LPWStr)] string path, ref Guid context);
        void GetGroupingParam(out Guid grouping); void SetGroupingParam(ref Guid grouping, ref Guid context);
        void RegisterAudioSessionNotification(IntPtr client); void UnregisterAudioSessionNotification(IntPtr client);
        void GetSessionIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetSessionInstanceIdentifier([MarshalAs(UnmanagedType.LPWStr)] out string id);
        void GetProcessId(out uint pid);
        [PreserveSig] int IsSystemSoundsSession();
        void SetDuckingPreference([MarshalAs(UnmanagedType.Bool)] bool optOut);
    }
    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface ISimpleAudioVolume
    {
        void SetMasterVolume(float level, ref Guid context); void GetMasterVolume(out float level);
        void SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        void GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }
}
