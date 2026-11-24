using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ReFix.SystemMonitor
{
    internal static class NativeMetrics
    {
        [StructLayout(LayoutKind.Sequential)] internal struct FileTime { public uint Low, High; public ulong Value { get { return ((ulong)High << 32) | Low; } } }
        [StructLayout(LayoutKind.Sequential)] internal struct MemoryStatus { public uint Length, Load; public ulong TotalPhysical, AvailablePhysical, TotalPageFile, AvailablePageFile, TotalVirtual, AvailableVirtual, AvailableExtendedVirtual; }
        [StructLayout(LayoutKind.Sequential)] internal struct IoCounters { public ulong ReadOperations, WriteOperations, OtherOperations, ReadBytes, WriteBytes, OtherBytes; }
        [DllImport("kernel32.dll")] internal static extern bool GetSystemTimes(out FileTime idle, out FileTime kernel, out FileTime user);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GlobalMemoryStatusEx(ref MemoryStatus status);
        [DllImport("kernel32.dll", SetLastError = true)] internal static extern bool GetProcessIoCounters(IntPtr process, out IoCounters counters);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhOpenQuery(string source, IntPtr data, out IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr data, out IntPtr counter);
        [DllImport("pdh.dll")] private static extern uint PdhCollectQueryData(IntPtr query);
        [DllImport("pdh.dll")] private static extern uint PdhCloseQuery(IntPtr query);
        [DllImport("pdh.dll", CharSet = CharSet.Unicode)] private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint size, out uint count, IntPtr items);
        [StructLayout(LayoutKind.Explicit)] private struct FormattedValue { [FieldOffset(0)] public uint Status; [FieldOffset(8)] public double Value; }
        [StructLayout(LayoutKind.Sequential)] private struct ArrayItem { public IntPtr Name; public FormattedValue Value; }

        // A single query owns a bounded set of wildcard counters; instance buffers are capped.
        internal sealed class Counters : IDisposable
        {
            private IntPtr query;
            private readonly Dictionary<string, IntPtr> handles = new Dictionary<string, IntPtr>();
            internal Counters()
            {
                if (PdhOpenQuery(null, IntPtr.Zero, out query) != 0) query = IntPtr.Zero;
                Add("diskBusy", @"\PhysicalDisk(*)\% Idle Time");
                Add("diskRead", @"\PhysicalDisk(*)\Disk Read Bytes/sec");
                Add("diskWrite", @"\PhysicalDisk(*)\Disk Write Bytes/sec");
                Add("gpu", @"\GPU Engine(*)\Utilization Percentage");
                Add("dedicated", @"\GPU Adapter Memory(*)\Dedicated Usage");
                Add("shared", @"\GPU Adapter Memory(*)\Shared Usage");
            }
            private void Add(string key, string path)
            {
                IntPtr handle;
                if (query != IntPtr.Zero && PdhAddEnglishCounter(query, path, IntPtr.Zero, out handle) == 0) handles[key] = handle;
            }
            internal bool Collect() { return query != IntPtr.Zero && PdhCollectQueryData(query) == 0; }
            internal Dictionary<string, double> Read(string key)
            {
                var result = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
                IntPtr handle;
                if (!handles.TryGetValue(key, out handle)) return result;
                uint size = 0, count;
                uint status = PdhGetFormattedCounterArray(handle, 0x200, ref size, out count, IntPtr.Zero);
                if (status != 0x800007D2 || size == 0 || size > 16 * 1024 * 1024) return result;
                IntPtr buffer = Marshal.AllocHGlobal((int)size);
                try
                {
                    if (PdhGetFormattedCounterArray(handle, 0x200, ref size, out count, buffer) != 0) return result;
                    int stride = Marshal.SizeOf(typeof(ArrayItem));
                    if ((ulong)count * (ulong)stride > size) return result;
                    for (int i = 0; i < count; i++)
                    {
                        var item = (ArrayItem)Marshal.PtrToStructure(IntPtr.Add(buffer, i * stride), typeof(ArrayItem));
                        if (item.Value.Status <= 1 && !Double.IsNaN(item.Value.Value) && !Double.IsInfinity(item.Value.Value))
                            result[Marshal.PtrToStringUni(item.Name)] = item.Value.Value;
                    }
                }
                finally { Marshal.FreeHGlobal(buffer); }
                return result;
            }
            public void Dispose() { if (query != IntPtr.Zero) { PdhCloseQuery(query); query = IntPtr.Zero; } }
        }
    }
}
