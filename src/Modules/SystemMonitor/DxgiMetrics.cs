using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
namespace ReFix.SystemMonitor
{
    internal static class DxgiMetrics
    {
        internal sealed class Adapter { internal string Id, Name; internal double Dedicated, Shared; }
        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct Description {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst=128)] public string Name;
            public uint Vendor, Device, Subsystem, Revision;
            public UIntPtr DedicatedVideo, DedicatedSystem, Shared;
            public uint LuidLow; public int LuidHigh; public uint Flags;
        }
        [DllImport("dxgi.dll")] private static extern int CreateDXGIFactory1(ref Guid iid, out IntPtr factory);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Enumerate(IntPtr self, uint index, out IntPtr adapter);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int Describe(IntPtr self, out Description description);
        private static Delegate Method(IntPtr instance, int slot, Type type) { return Marshal.GetDelegateForFunctionPointer(Marshal.ReadIntPtr(Marshal.ReadIntPtr(instance), slot * IntPtr.Size), type); }
        internal static List<Adapter> Read()
        {
            var result = new List<Adapter>(); IntPtr factory = IntPtr.Zero;
            try {
                Guid iid = new Guid("770aae78-f26f-4dba-a829-253c83d1b387");
                if (CreateDXGIFactory1(ref iid, out factory) < 0) return result;
                var enumerate = (Enumerate)Method(factory,12,typeof(Enumerate));
                for(uint index=0; index<32; index++) {
                    IntPtr adapter; if(enumerate(factory,index,out adapter)<0) break;
                    try { Description d; if(((Describe)Method(adapter,10,typeof(Describe)))(adapter,out d)<0 || (d.Flags & 2)!=0) continue;
                        result.Add(new Adapter { Id="0x"+unchecked((uint)d.LuidHigh).ToString("x8")+"_0x"+d.LuidLow.ToString("x8")+"_0", Name=d.Name, Dedicated=d.DedicatedVideo.ToUInt64(), Shared=d.Shared.ToUInt64() });
                    } finally { Marshal.Release(adapter); }
                }
            } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { } catch (COMException) { }
            finally { if(factory!=IntPtr.Zero) Marshal.Release(factory); }
            return result;
        }
    }
}
