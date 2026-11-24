using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Management;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace ReFix.SystemMonitor
{
    public sealed class Metric
    {
        public string Id, Name, Kind, Detail;
        public double? Percent, Temperature, FrequencyGHz, UsedBytes, TotalBytes, ReadBytesPerSecond, WriteBytesPerSecond;
    }
    public sealed class ProcessMetric
    {
        public string Id, Name;
        public int Pid;
        public bool Background;
        public DateTime? Started;
        public TimeSpan Runtime;
        public double? CpuPercent, MemoryBytes, GpuPercent, ReadBytesPerSecond, WriteBytesPerSecond;
    }
    public sealed class Snapshot
    {
        public DateTime Timestamp;
        public List<Metric> Metrics = new List<Metric>();
        public List<ProcessMetric> Processes = new List<ProcessMetric>();
        public List<string> Warnings = new List<string>();
    }

    // Call synchronously from one worker. No timers, parallel WMI queries or process handles retained.
    public sealed class Collector : IDisposable
    {
        private readonly NativeMetrics.Counters counters = new NativeMetrics.Counters();
        private readonly Dictionary<int, PreviousProcess> previous = new Dictionary<int, PreviousProcess>();
        private readonly Dictionary<string, string> diskNames = new Dictionary<string, string>();
        private readonly Dictionary<string, double> temperatures = new Dictionary<string, double>();
        private readonly Dictionary<string, string> gpuNames = new Dictionary<string, string>();
        private List<DxgiMetrics.Adapter> dxgi = new List<DxgiMetrics.Adapter>(); private string cpuDetail; private long lastMetadata, lastSensors;
        private string cpuName = "CPU";
        private double? frequency;
        private ulong oldIdle, oldTotal;
        private bool cpuPrimed, disposed;
        private readonly Stopwatch clock = Stopwatch.StartNew();
        private sealed class PreviousProcess { internal long Started; internal long? Cpu; internal double Time; internal ulong? Read, Write; }
        private static double Clamp(double n) { return Math.Max(0, Math.Min(100, n)); }
        public Snapshot Collect(bool processes)
        {
            if (disposed) throw new ObjectDisposedException("Collector");
            var s = new Snapshot { Timestamp = DateTime.Now };
            if (lastMetadata == 0 || clock.ElapsedMilliseconds - lastMetadata >= 30000) RefreshMetadata();
            if (lastSensors == 0 || clock.ElapsedMilliseconds - lastSensors >= 5000) RefreshSensors();
            var cpu = new Metric { Id = "cpu", Kind = "cpu", Name = cpuName, Detail = cpuDetail, FrequencyGHz = frequency, Temperature = Temperature("cpu") };
            NativeMetrics.FileTime idle, kernel, user;
            if (NativeMetrics.GetSystemTimes(out idle, out kernel, out user))
            {
                ulong total = kernel.Value + user.Value;
                if (cpuPrimed && total > oldTotal && idle.Value >= oldIdle)
                    cpu.Percent = Clamp(100.0 * (1.0 - (double)(idle.Value - oldIdle) / (total - oldTotal)));
                oldIdle = idle.Value; oldTotal = total; cpuPrimed = true;
            }
            if (frequency.HasValue) cpu.Detail = cpuDetail + "; частота WMI (обновляется каждые 30 с)"; s.Metrics.Add(cpu);
            var memory = new Metric { Id = "memory", Kind = "memory", Name = "RAM" };
            var state = new NativeMetrics.MemoryStatus { Length = (uint)Marshal.SizeOf(typeof(NativeMetrics.MemoryStatus)) };
            if (NativeMetrics.GlobalMemoryStatusEx(ref state))
            {
                memory.TotalBytes = state.TotalPhysical; memory.UsedBytes = state.TotalPhysical - state.AvailablePhysical;
                if (state.TotalPhysical > 0) memory.Percent = 100.0 * memory.UsedBytes / state.TotalPhysical;
            }
            s.Metrics.Add(memory);
            bool ready = counters.Collect();
            var busy = ready ? counters.Read("diskBusy") : new Dictionary<string, double>();
            var reads = ready ? counters.Read("diskRead") : new Dictionary<string, double>();
            var writes = ready ? counters.Read("diskWrite") : new Dictionary<string, double>();
            var disks = new HashSet<string>(busy.Keys);
            foreach (string index in diskNames.Keys) { bool found = false; foreach (string name in disks) if (DiskIndex(name) == index) { found = true; break; } if (!found) disks.Add(index); }
            foreach (string disk in disks)
            {
                if (disk == "_Total") continue;
                string index = DiskIndex(disk), label;
                var metric = new Metric { Id = "disk:" + index, Kind = "disk", Name = diskNames.TryGetValue(index, out label) ? label : "Disk " + disk, Detail = disk, Temperature = Temperature("disk:" + index) };
                double value;
                if (busy.TryGetValue(disk, out value)) metric.Percent = Clamp(100 - value);
                if (reads.TryGetValue(disk, out value)) metric.ReadBytesPerSecond = Math.Max(0, value);
                if (writes.TryGetValue(disk, out value)) metric.WriteBytesPerSecond = Math.Max(0, value);
                s.Metrics.Add(metric);
            }
            var engines = ready ? counters.Read("gpu") : new Dictionary<string, double>();
            var dedicated = ready ? counters.Read("dedicated") : new Dictionary<string, double>();
            var shared = ready ? counters.Read("shared") : new Dictionary<string, double>();
            var adapterLoad = new Dictionary<string, double>();
            var engineLoad = new Dictionary<string, double>();
            foreach (var entry in engines)
            {
                string adapter = Adapter(entry.Key); if (adapter == null) continue;
                var match = Regex.Match(entry.Key, @"_eng_(\d+)");
                string engine = adapter + ":" + (match.Success ? match.Groups[1].Value : entry.Key);
                double value; engineLoad.TryGetValue(engine, out value); engineLoad[engine] = value + entry.Value;
            }
            foreach (var e in engineLoad) { string adapter = e.Key.Substring(0, e.Key.IndexOf(':')); double value; adapterLoad.TryGetValue(adapter, out value); adapterLoad[adapter] = Math.Max(value, e.Value); }
            var adapters = new HashSet<string>(adapterLoad.Keys);
            foreach (string key in dedicated.Keys) { string a = Adapter(key); if (a != null) adapters.Add(a); }
            foreach (string key in shared.Keys) { string a = Adapter(key); if (a != null) adapters.Add(a); }
            foreach (string adapter in adapters)
            {
                double value;
                var gpu = new Metric { Id = "gpu:" + adapter, Kind = "gpu", Name = "GPU " + adapter, Detail = "Dedicated + shared memory", Temperature = adapters.Count == 1 ? Temperature("gpu") : null };
                if (adapterLoad.TryGetValue(adapter, out value)) gpu.Percent = Clamp(value);
                foreach (var hardware in dxgi) if (String.Equals(hardware.Id, adapter, StringComparison.OrdinalIgnoreCase)) { gpu.Name = hardware.Name; break; }
                if (gpu.Name.StartsWith("GPU ") && gpuNames.Count == 1 && adapters.Count == 1) foreach (var name in gpuNames.Values) gpu.Name = name;
                double bytes = 0; bool hasMemory = false; double dedicatedBytes = 0, sharedBytes = 0;
                foreach (var e in dedicated) if (Adapter(e.Key) == adapter) { bytes += e.Value; dedicatedBytes += e.Value; hasMemory = true; }
                foreach (var e in shared) if (Adapter(e.Key) == adapter) { bytes += e.Value; sharedBytes += e.Value; hasMemory = true; }
                if (hasMemory) { gpu.UsedBytes = bytes; foreach (var hardware in dxgi) if (String.Equals(hardware.Id, adapter, StringComparison.OrdinalIgnoreCase) && hardware.Dedicated > 0) { gpu.UsedBytes = dedicatedBytes; gpu.TotalBytes = hardware.Dedicated; break; } gpu.Detail = String.Format(CultureInfo.CurrentCulture, "Выделенная: {0:F0} МиБ; общая: {1:F0} МиБ", dedicatedBytes / 1048576, sharedBytes / 1048576); }
                s.Metrics.Add(gpu);
            }
            if (adapters.Count == 0) foreach (var g in gpuNames) s.Metrics.Add(new Metric { Id = "gpu:" + g.Key, Kind = "gpu", Name = g.Value, Temperature = gpuNames.Count == 1 ? Temperature("gpu") : null });
            if (processes) CollectProcesses(s, engines); else previous.Clear();
            if (cpu.Temperature == null) s.Warnings.Add("CPU temperature unavailable: no readable hardware sensor provider.");
            if (busy.Count == 0) s.Warnings.Add("Disk activity counters unavailable or warming up.");
            if (engines.Count == 0) s.Warnings.Add("GPU activity counters unavailable or warming up.");
            return s;
        }
        private static string DiskIndex(string instance) { int space = instance.IndexOf(' '); return space < 0 ? instance : instance.Substring(0, space); }
        private static string Adapter(string instance) { var m = Regex.Match(instance, @"luid_(0x[0-9a-f]+_0x[0-9a-f]+)_phys_(\d+)", RegexOptions.IgnoreCase); return m.Success ? m.Groups[1].Value + "_" + m.Groups[2].Value : null; }
        private double? Temperature(string key) { double value; return temperatures.TryGetValue(key, out value) ? (double?)value : null; }
        private void RefreshMetadata()
        {
            lastMetadata = Math.Max(1, clock.ElapsedMilliseconds); dxgi = DxgiMetrics.Read();
            Query("root\\cimv2", "SELECT Name,CurrentClockSpeed,NumberOfCores,NumberOfLogicalProcessors FROM Win32_Processor", delegate(ManagementBaseObject o) { cpuName = Convert.ToString(o["Name"]); cpuDetail = "Ядер: " + Convert.ToString(o["NumberOfCores"]) + "; потоков: " + Convert.ToString(o["NumberOfLogicalProcessors"]); double mhz; if (Double.TryParse(Convert.ToString(o["CurrentClockSpeed"]), out mhz) && mhz > 0) frequency = mhz / 1000; });
            diskNames.Clear();
            Query("root\\cimv2", "SELECT Index,Model FROM Win32_DiskDrive", delegate(ManagementBaseObject o) { diskNames[Convert.ToString(o["Index"])] = Convert.ToString(o["Model"]); });
            gpuNames.Clear();
            Query("root\\cimv2", "SELECT DeviceID,Name FROM Win32_VideoController", delegate(ManagementBaseObject o) { gpuNames[Convert.ToString(o["DeviceID"])] = Convert.ToString(o["Name"]); });
        }
        private void RefreshSensors()
        {
            lastSensors = Math.Max(1, clock.ElapsedMilliseconds); temperatures.Clear();
            foreach (string provider in new[] { "root\\LibreHardwareMonitor", "root\\OpenHardwareMonitor" })
                Query(provider, "SELECT Name,Identifier,Value FROM Sensor WHERE SensorType='Temperature'", delegate(ManagementBaseObject o)
                {
                    string id = Convert.ToString(o["Identifier"]).ToLowerInvariant(), name = Convert.ToString(o["Name"]).ToLowerInvariant();
                    double value = Convert.ToDouble(o["Value"], CultureInfo.InvariantCulture);
                    if (Double.IsNaN(value) || value < -30 || value > 150) return;
                    string key = id.Contains("cpu") ? "cpu" : id.Contains("gpu") ? "gpu" : null;
                    if (key != null && (name.Contains("package") || name.Contains("core") || !temperatures.ContainsKey(key)))
                    { double old; temperatures.TryGetValue(key, out old); temperatures[key] = Math.Max(old, value); }
                });
        }
        private static void Query(string scope, string query, Action<ManagementBaseObject> visit)
        {
            try { var options = new EnumerationOptions { Timeout = TimeSpan.FromSeconds(2), ReturnImmediately = false }; using (var search = new ManagementObjectSearcher(scope, query, options)) using (var results = search.Get()) foreach (ManagementBaseObject obj in results) using (obj) { visit(obj); } } catch (ManagementException) { } catch (UnauthorizedAccessException) { } catch (COMException) { }
        }
        private void CollectProcesses(Snapshot snapshot, Dictionary<string, double> engines)
        {
            var current = new Dictionary<int, PreviousProcess>();
            double now = clock.Elapsed.TotalSeconds;
            foreach (Process p in Process.GetProcesses()) using (p)
            {
                try
                {
                    var metric = new ProcessMetric { Pid = p.Id, Name = p.ProcessName, Id = p.Id.ToString(CultureInfo.InvariantCulture) };
                    var sample = new PreviousProcess { Time = now };
                    try { metric.Started = p.StartTime; sample.Started = metric.Started.Value.ToUniversalTime().Ticks; metric.Runtime = snapshot.Timestamp - metric.Started.Value; metric.Id += ":" + sample.Started; } catch (Exception ex) { if (!(ex is System.ComponentModel.Win32Exception || ex is InvalidOperationException || ex is NotSupportedException)) throw; }
                    try { metric.Background = p.MainWindowHandle == IntPtr.Zero; metric.MemoryBytes = p.WorkingSet64; sample.Cpu = p.TotalProcessorTime.Ticks; } catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { }
                    NativeMetrics.IoCounters io;
                    try { if (NativeMetrics.GetProcessIoCounters(p.Handle, out io)) { sample.Read = io.ReadBytes; sample.Write = io.WriteBytes; } } catch (System.ComponentModel.Win32Exception) { } catch (InvalidOperationException) { }
                    PreviousProcess before;
                    if (sample.Started != 0 && previous.TryGetValue(p.Id, out before) && before.Started == sample.Started && now > before.Time)
                    {
                        double seconds = now - before.Time;
                        if (sample.Cpu.HasValue && before.Cpu.HasValue && sample.Cpu >= before.Cpu) metric.CpuPercent = Clamp((sample.Cpu.Value - before.Cpu.Value) / (seconds * TimeSpan.TicksPerSecond * Environment.ProcessorCount) * 100);
                        if (sample.Read.HasValue && before.Read.HasValue && sample.Read >= before.Read) metric.ReadBytesPerSecond = (sample.Read.Value - before.Read.Value) / seconds;
                        if (sample.Write.HasValue && before.Write.HasValue && sample.Write >= before.Write) metric.WriteBytesPerSecond = (sample.Write.Value - before.Write.Value) / seconds;
                    }
                    string prefix = "pid_" + p.Id + "_"; double gpu = 0; bool found = false;
                    foreach (var e in engines) if (e.Key.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) { gpu = Math.Max(gpu, e.Value); found = true; }
                    if (found) metric.GpuPercent = Clamp(gpu);
                    current[p.Id] = sample; snapshot.Processes.Add(metric);
                }
                catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            }
            previous.Clear(); foreach (var item in current) previous[item.Key] = item.Value;
        }
        public void Dispose() { if (!disposed) { disposed = true; counters.Dispose(); previous.Clear(); } }
    }
}
