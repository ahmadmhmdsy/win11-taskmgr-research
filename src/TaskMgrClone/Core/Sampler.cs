using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace TaskMgrClone.Core;

public sealed class SystemSnapshot
{
    public double CpuPercent;
    public double CpuSpeedGhz;
    public double CpuBaseGhz;
    public int CpuCores;
    public int CpuLogical;
    public int CpuProcesses;
    public int CpuThreads;
    public int CpuHandles;
    public TimeSpan Uptime;
    public double[] PerCorePercent = Array.Empty<double>();

    public ulong MemTotal;
    public ulong MemAvailable;
    public ulong MemInUse;
    public ulong MemCached;
    public ulong MemCompressed;
    public ulong MemCommit;
    public ulong MemCommitLimit;
    public ulong MemPagedPool;
    public ulong MemNonPagedPool;
    public ulong MemStandby;
    public ulong MemFree;
    public ulong MemModified;
    public ulong MemHardwareReserved;
    public int MemTotalSlots;
    public int MemUsedSlots;
    public string MemFormFactor = "";
    public string MemSpeed = "";
    public double MemPercentOfTotal => MemTotal == 0 ? 0 : 100.0 * (MemInUse + MemCompressed) / MemTotal;

    public double DiskActiveTotal;
    public List<DiskVolume> Disks { get; } = new();
    public List<NetworkAdapter> Networks { get; } = new();
    public List<GpuAdapterInfo> Gpus { get; } = new();
}

public sealed class DiskVolume
{
    public string Instance { get; set; } = "";
    public string Label { get; set; } = "";
    public string MediaType { get; set; } = "";
    public double ActivePercent;
    public double ReadBytesPerSec;
    public double WriteBytesPerSec;
    public double AvgResponseMs;
    public double QueueLength;
    public ulong Capacity;
    public ulong FreeSpace;
    public double ReadPercent => Math.Min(100, ActivePercent);
    public double WritePercent => Math.Min(100, AvgResponseMs * 20);
}

public sealed class NetworkAdapter
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = "Ethernet";
    public double TotalBytesPerSec;
    public double ReceiveBytesPerSec;
    public double SendBytesPerSec;
    public double LinkSpeedBps;
    public string Ipv4 = "";
    public string Ipv6 = "";
}

/// <summary>
/// Samples system + per-process counters on a timer and exposes immutable snapshots.
/// </summary>
public sealed class Sampler : IDisposable
{
    private System.Threading.Timer? _timer;
    private int _intervalMs = 1000;
    private int _ticking;

    private ulong _prevIdle, _prevKernel, _prevUser;
    private readonly Dictionary<int, ulong> _prevProcTime = new();
    private readonly Dictionary<int, ulong> _prevDisk = new();
    private readonly Dictionary<int, ulong> _prevProcTimeForHistory = new();

    private readonly PdhQuery _pdh = new();
    private readonly PdhQuery _gpuPdh = new();
    private bool _pdhReady;

    private readonly List<GpuAdapterInfo> _gpuAdapters = new();
    private bool _loadedGpuAdapters;
    private readonly RingBuffer _idleCpuTrace = new(240);

    public event Action? Sampled;

    public SystemSnapshot System { get; } = new();
    public Dictionary<int, ProcessRow> Processes { get; } = new();
    public int IntervalMs
    {
        get => _intervalMs;
        set { _intervalMs = Math.Clamp(value, 250, 4000); RestartTimer(); }
    }
    public bool Paused { get; set; }

    public Sampler()
    {
        for (int i = 0; i < 1024; i++) _idleCpuTrace.Add(0);
    }

    public void Start()
    {
        RestartTimer();
    }

    /// <summary>Runs one sampling pass synchronously (used by --sampledump diagnostics and tests).</summary>
    public void SampleOnce() => Tick();

    private void RestartTimer()
    {
        _timer?.Dispose();
        _timer = new System.Threading.Timer(_ => Tick(), null, 0, _intervalMs);
    }

    private void Tick()
    {
        if (Paused) { Sampled?.Invoke(); return; }
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try
        {
            EnsurePdh();
            CollectSystem();
            CollectProcesses();
            Sampled?.Invoke();
        }
        catch { /* sampling must never kill the app */ }
        finally { Interlocked.Exchange(ref _ticking, 0); }
    }

    private void EnsurePdh()
    {
        if (_pdhReady) return;
        _pdh.AddCounter("cpuUtil", @"\Processor Information(_Total)\% Processor Utility");
        _pdh.AddCounter("cpuPerf", @"\Processor Information(_Total)\% Processor Performance");
        _pdh.AddCounter("cpuIdle", @"\Processor Information(_Total)\% Idle Time");
        _pdh.AddCounter("coreUtil", @"\Processor Information(*)\% Processor Utility");
        _pdh.AddCounter("memModified", @"\Memory\Modified Page List Bytes");
        _pdh.AddCounter("memStandby", @"\Memory\Standby Cache Normal Priority Bytes");
        _pdh.AddCounter("memStandbyCore", @"\Memory\Standby Cache Core Bytes");
        _pdh.AddCounter("memStandbyReserve", @"\Memory\Standby Cache Reserve Bytes");
        _pdh.AddCounter("memFree", @"\Memory\Free & Zero Page List Bytes");
        _pdh.AddCounter("memPoolPaged", @"\Memory\Pool Paged Bytes");
        _pdh.AddCounter("memPoolNonPaged", @"\Memory\Pool Nonpaged Bytes");
        _pdh.AddCounter("memCache", @"\Memory\Cache Bytes");
        _pdh.AddCounter("memCommit", @"\Memory\Committed Bytes");
        _pdh.AddCounter("memCommitLimit", @"\Memory\Commit Limit");
        _pdh.AddCounter("diskActive", @"\PhysicalDisk(*)\% Disk Time");
        _pdh.AddCounter("diskRead", @"\PhysicalDisk(*)\Disk Read Bytes/sec");
        _pdh.AddCounter("diskWrite", @"\PhysicalDisk(*)\Disk Write Bytes/sec");
        _pdh.AddCounter("diskLatency", @"\PhysicalDisk(*)\Avg. Disk sec/Transfer");
        _pdh.AddCounter("diskQueue", @"\PhysicalDisk(*)\Current Disk Queue Length");
        _pdh.AddCounter("netTotal", @"\Network Interface(*)\Bytes Total/sec");
        _pdh.AddCounter("netRecv", @"\Network Interface(*)\Bytes Received/sec");
        _pdh.AddCounter("netSend", @"\Network Interface(*)\Bytes Sent/sec");
        _pdh.AddCounter("netSpeed", @"\Network Interface(*)\Current Bandwidth");
        _pdh.AddCounter("gpuEngine", @"\GPU Engine(*)\Utilization Percentage");
        _pdh.AddCounter("gpuDedicated", @"\GPU Adapter Memory(*)\Dedicated Usage");
        _pdh.AddCounter("gpuShared", @"\GPU Adapter Memory(*)\Shared Usage");
        _pdh.AddCounter("gpuTemp", @"\GPU(*)\Temperature");
        _pdh.Collect();

        _gpuPdh.AddCounter("gpuEngine", @"\GPU Engine(*)\Utilization Percentage");
        _gpuPdh.AddCounter("gpuProcMem", @"\GPU Process Memory(*)\Dedicated Usage");
        _gpuPdh.AddCounter("gpuProcMemShared", @"\GPU Process Memory(*)\Shared Usage");
        _gpuPdh.Collect();
        _pdhReady = true;
    }

    private void CollectSystem()
    {
        Native.GetSystemTimes(out var idle, out var kernel, out var user);
        var sys = System;
        if (_prevKernel != 0)
        {
            var idleDelta = idle.Value - _prevIdle;
            var kernelDelta = kernel.Value - _prevKernel;
            var userDelta = user.Value - _prevUser;
            var total = kernelDelta + userDelta;
            sys.CpuPercent = total == 0 ? 0 : Math.Clamp(100.0 * (total - idleDelta) / total, 0, 100);
            _idleCpuTrace.Add(sys.CpuPercent);
        }
        _prevIdle = idle.Value; _prevKernel = kernel.Value; _prevUser = user.Value;

        var mem = new Native.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Native.MEMORYSTATUSEX>() };
        if (Native.GlobalMemoryStatusEx(ref mem))
        {
            sys.MemTotal = mem.ullTotalPhys;
            sys.MemAvailable = mem.ullAvailPhys;
            sys.MemCommit = mem.ullTotalPageFile - mem.ullAvailPageFile;
            sys.MemCommitLimit = mem.ullTotalPageFile;
        }
        if (Native.GetPerformanceInfo(out var perf, (uint)Marshal.SizeOf<Native.PERFORMANCE_INFORMATION>()))
        {
            sys.CpuProcesses = (int)perf.ProcessCount;
            sys.CpuThreads = (int)perf.ThreadCount;
            sys.CpuHandles = (int)perf.HandleCount;
            sys.MemPagedPool = (ulong)perf.KernelPaged * perf.PageSize;
            sys.MemNonPagedPool = (ulong)perf.KernelNonPaged * perf.PageSize;
            if (sys.MemTotal == 0) sys.MemTotal = (ulong)perf.PhysicalTotal * perf.PageSize;
        }
        sys.Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        sys.CpuCores = _coreCount == 0 ? (_coreCount = Native.PhysicalCoreCount()) : _coreCount;
        sys.CpuLogical = Environment.ProcessorCount;
        if (sys.CpuBaseGhz <= 0) sys.CpuBaseGhz = ReadCpuBaseGhz();

        if (!_pdh.Collect()) { FillFallbackStats(); return; }
        var perfValue = _pdh.GetSingle("cpuPerf");
        sys.CpuSpeedGhz = perfValue > 0 ? sys.CpuBaseGhz * perfValue / 100.0 : sys.CpuBaseGhz;
        sys.MemModified = (ulong)Math.Max(0, _pdh.GetSingle("memModified"));
        sys.MemStandby = (ulong)(Math.Max(0, _pdh.GetSingle("memStandby")) + Math.Max(0, _pdh.GetSingle("memStandbyCore")) + Math.Max(0, _pdh.GetSingle("memStandbyReserve")));
        sys.MemFree = (ulong)Math.Max(0, _pdh.GetSingle("memFree"));
        sys.MemCached = (ulong)Math.Max(0, _pdh.GetSingle("memCache"));
        if (sys.MemPagedPool == 0) sys.MemPagedPool = (ulong)Math.Max(0, _pdh.GetSingle("memPoolPaged"));
        if (sys.MemNonPagedPool == 0) sys.MemNonPagedPool = (ulong)Math.Max(0, _pdh.GetSingle("memPoolNonPaged"));
        var commit = _pdh.GetSingle("memCommit");
        if (commit > 0) sys.MemCommit = (ulong)commit;
        var limit = _pdh.GetSingle("memCommitLimit");
        if (limit > 0) sys.MemCommitLimit = (ulong)limit;
        sys.MemInUse = sys.MemTotal > sys.MemAvailable ? sys.MemTotal - sys.MemAvailable : 0;
        sys.MemCompressed = Math.Max(0, Math.Min(sys.MemModified, sys.MemInUse / 10));

        var cores = _pdh.GetArray("coreUtil");
        var coreValues = cores.Where(c => c.Instance != "_Total").Select(c => Math.Clamp(c.Value, 0, 100)).ToArray();
        if (coreValues.Length > 0) sys.PerCorePercent = coreValues;

        CollectDisks();
        CollectNetwork();
        CollectGpu();
        _ = sys.CpuHandles;
    }

    private int _coreCount;
    private int _sampleTick;

    private static double ReadCpuBaseGhz()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            if (key?.GetValue("~MHz") is int mhz && mhz > 0) return mhz / 1000.0;
            if (key?.GetValue("~MHz") is uint umhz && umhz > 0) return umhz / 1000.0;
        }
        catch { }
        return 0;
    }

    private void FillFallbackStats()
    {
        System.MemInUse = System.MemTotal > System.MemAvailable ? System.MemTotal - System.MemAvailable : 0;
    }

    private void CollectDisks()
    {
        var sys = System;
        var actives = _pdh.GetArray("diskActive");
        var reads = _pdh.GetArray("diskRead");
        var writes = _pdh.GetArray("diskWrite");
        var latency = _pdh.GetArray("diskLatency");
        var queues = _pdh.GetArray("diskQueue");
        double total = 0;
        var seen = new List<DiskVolume>();
        foreach (var (instance, active) in actives)
        {
            if (instance == "_Total") { total = active; continue; }
            var volume = new DiskVolume
            {
                Instance = instance,
                ActivePercent = Math.Clamp(active, 0, 100),
                ReadBytesPerSec = reads.FirstOrDefault(r => r.Instance == instance).Value,
                WriteBytesPerSec = writes.FirstOrDefault(r => r.Instance == instance).Value,
                AvgResponseMs = latency.FirstOrDefault(r => r.Instance == instance).Value * 1000.0,
                QueueLength = queues.FirstOrDefault(r => r.Instance == instance).Value,
                Label = DiskLabel(instance),
            };
            var diskToken = instance.Split(' ', 2)[0];
            if (int.TryParse(diskToken, out var diskNumber)) volume.MediaType = Win32Ext.GetDiskMediaType(diskNumber);
            foreach (var token in volume.Label.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if (token.Length < 2 || token[^1] != ':' || !char.IsLetter(token[0])) continue;
                try
                {
                    var di = new DriveInfo(char.ToUpperInvariant(token[0]).ToString());
                    if (!di.IsReady) continue;
                    volume.Capacity += (ulong)di.TotalSize;
                    volume.FreeSpace += (ulong)di.AvailableFreeSpace;
                }
                catch { }
            }
            seen.Add(volume);
        }
        sys.DiskActiveTotal = total > 0 ? total : seen.Sum(v => v.ActivePercent);
        if (seen.Count > 0)
        {
            sys.Disks.Clear();
            sys.Disks.AddRange(seen);
        }
    }

    internal static string DiskLabel(string instance)
    {
        // instance looks like "0 C:" or "1 E: F: G:"
        var parts = instance.Split(' ', 2);
        return parts.Length < 2 ? instance : parts[1];
    }

    private void CollectNetwork()
    {
        var sys = System;
        var totals = _pdh.GetArray("netTotal");
        var recv = _pdh.GetArray("netRecv");
        var send = _pdh.GetArray("netSend");
        var speed = _pdh.GetArray("netSpeed");
        var adapters = new List<NetworkAdapter>();
        foreach (var (instance, value) in totals)
        {
            if (IsVirtualAdapter(instance)) continue;
            if (value <= 0.01) continue;
            adapters.Add(new NetworkAdapter
            {
                Name = instance,
                Kind = AdapterKind(instance),
                TotalBytesPerSec = value,
                ReceiveBytesPerSec = recv.FirstOrDefault(r => r.Instance == instance).Value,
                SendBytesPerSec = send.FirstOrDefault(r => r.Instance == instance).Value,
                LinkSpeedBps = speed.FirstOrDefault(r => r.Instance == instance).Value,
            });
        }
        FillAdapterDetails(adapters);
        if (adapters.Count > 0 || sys.Networks.Count == 0)
        {
            sys.Networks.Clear();
            sys.Networks.AddRange(adapters.OrderByDescending(a => a.TotalBytesPerSec));
        }
    }

    private static bool IsVirtualAdapter(string name) =>
        name.Contains("Loopback", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("isatap", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Teredo", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Pseudo-Interface", StringComparison.OrdinalIgnoreCase);

    private static void FillAdapterDetails(List<NetworkAdapter> adapters)
    {
        try
        {
            var nics = NetworkInterface.GetAllNetworkInterfaces();
            foreach (var adapter in adapters)
            {
                var nic = nics.FirstOrDefault(n =>
                    n.Name.Contains(adapter.Name, StringComparison.OrdinalIgnoreCase) ||
                    adapter.Name.Contains(n.Name, StringComparison.OrdinalIgnoreCase) ||
                    (n.Description.Length > 0 && adapter.Name.Contains(n.Description, StringComparison.OrdinalIgnoreCase)));
                if (nic == null) continue;
                try
                {
                    if (nic.Speed > 0) adapter.LinkSpeedBps = nic.Speed;
                    var props = nic.GetIPProperties();
                    adapter.Ipv4 = props.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address.ToString() ?? "";
                    adapter.Ipv6 = props.UnicastAddresses
                        .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetworkV6 && !a.Address.IsIPv6LinkLocal)?.Address.ToString() ?? "";
                }
                catch { }
            }
        }
        catch { }
    }

    private static string AdapterKind(string name)
    {
        if (name.Contains("Wi-Fi", StringComparison.OrdinalIgnoreCase) || name.Contains("Wireless", StringComparison.OrdinalIgnoreCase) || name.Contains("802.11", StringComparison.OrdinalIgnoreCase)) return "Wi-Fi";
        if (name.Contains("Bluetooth", StringComparison.OrdinalIgnoreCase)) return "Bluetooth";
        if (name.Contains("Ethernet", StringComparison.OrdinalIgnoreCase)) return "Ethernet";
        return "Ethernet";
    }

    private void CollectGpu()
    {
        LoadGpuAdapters();
        var engines = _pdh.GetArray("gpuEngine");
        var dedicated = _pdh.GetArray("gpuDedicated");
        var shared = _pdh.GetArray("gpuShared");
        var temps = _pdh.GetArray("gpuTemp");

        var byLuid = new Dictionary<string, GpuAdapterInfo>(StringComparer.OrdinalIgnoreCase);
        foreach (var adapter in _gpuAdapters)
        {
            adapter.Percent3D = adapter.PercentCopy = adapter.PercentVideoEncode = adapter.PercentVideoDecode = adapter.PercentCompute = 0;
            adapter.DedicatedUsed = 0;
            if (!string.IsNullOrEmpty(adapter.Luid)) byLuid[adapter.Luid] = adapter;
        }

        foreach (var (instance, value) in engines)
        {
            var luid = ExtractLuid(instance);
            if (luid.Length == 0 || !byLuid.TryGetValue(luid, out var adapter)) continue;
            if (instance.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase)) adapter.Percent3D += value;
            else if (instance.Contains("engtype_Copy", StringComparison.OrdinalIgnoreCase)) adapter.PercentCopy += value;
            else if (instance.Contains("engtype_VideoEncode", StringComparison.OrdinalIgnoreCase)) adapter.PercentVideoEncode += value;
            else if (instance.Contains("engtype_VideoDecode", StringComparison.OrdinalIgnoreCase)) adapter.PercentVideoDecode += value;
            else if (instance.Contains("engtype_Compute", StringComparison.OrdinalIgnoreCase)) adapter.PercentCompute += value;
        }
        foreach (var (instance, value) in dedicated)
        {
            var luid = ExtractLuid(instance);
            if (luid.Length > 0 && byLuid.TryGetValue(luid, out var adapter)) adapter.DedicatedUsed = (ulong)Math.Max(0, value);
        }
        foreach (var (instance, value) in temps)
        {
            var luid = ExtractLuid(instance);
            if (luid.Length > 0 && byLuid.TryGetValue(luid, out var adapter)) adapter.Temperature = value > 0 ? value : null;
        }
        foreach (var adapter in _gpuAdapters)
        {
            adapter.Percent3D = Math.Clamp(adapter.Percent3D, 0, 100);
            adapter.PercentCopy = Math.Clamp(adapter.PercentCopy, 0, 100);
            adapter.PercentVideoEncode = Math.Clamp(adapter.PercentVideoEncode, 0, 100);
            adapter.PercentVideoDecode = Math.Clamp(adapter.PercentVideoDecode, 0, 100);
            adapter.PercentCompute = Math.Clamp(adapter.PercentCompute, 0, 100);
        }
        if (_gpuAdapters.Count > 0)
        {
            System.Gpus.Clear();
            System.Gpus.AddRange(_gpuAdapters);
        }
        _ = shared;
    }

    internal static string ExtractLuid(string instance)
    {
        var idx = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
        if (idx < 0) return "";
        var rest = instance[idx..];
        var end = rest.IndexOf("_phys", StringComparison.OrdinalIgnoreCase);
        return end > 0 ? rest[..end] : rest;
    }

    private void LoadGpuAdapters()
    {
        if (_loadedGpuAdapters) return;
        _loadedGpuAdapters = true;
        try
        {
            using var cls = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");
            if (cls == null) return;
            foreach (var name in cls.GetSubKeyNames().Where(n => n.Length == 4 && n.All(char.IsDigit)))
            {
                using var key = cls.OpenSubKey(name);
                if (key == null) continue;
                var desc = key.GetValue("DriverDesc") as string;
                if (string.IsNullOrWhiteSpace(desc)) continue;
                var adapter = new GpuAdapterInfo
                {
                    Name = desc,
                    DriverVersion = key.GetValue("DriverVersion") as string ?? "",
                };
                var mem = key.GetValue("HardwareInformation.qwMemorySize");
                if (mem is byte[] bytes && bytes.Length >= 8) adapter.DedicatedMemory = BitConverter.ToUInt64(bytes, 0);
                else if (mem is long l) adapter.DedicatedMemory = (ulong)l;
                adapter.SharedMemory = System.MemTotal / 2;
                _gpuAdapters.Add(adapter);
            }
            // Attach adapter LUIDs in PDH enumeration order.
            var luids = _pdh.GetArray("gpuDedicated").Select(d => ExtractLuid(d.Instance)).Where(l => l.Length > 0).Distinct().ToList();
            for (int i = 0; i < _gpuAdapters.Count && i < luids.Count; i++) _gpuAdapters[i].Luid = luids[i];
        }
        catch { }
    }

    private void CollectProcesses()
    {
        var now = DateTime.Now;
        var totalCpuSeconds = (double)(_prevKernel + _prevUser) / 10_000_000.0;
        var wallDelta = Math.Max(0.001, _intervalMs / 1000.0);
        var windowOwners = Win32Ext.VisibleWindowOwners();
        var seen = new HashSet<int>();
        var byPid = new Dictionary<int, (string Name, int ParentPid, int Threads, int BasePriority)>();
        var snapshot = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
        if (snapshot != IntPtr.Zero)
        {
            var entry = new Native.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<Native.PROCESSENTRY32>() };
            if (Native.Process32First(snapshot, ref entry))
            {
                do
                {
                    byPid[(int)entry.th32ProcessID] = (entry.szExeFile ?? "", (int)entry.th32ParentProcessID, (int)entry.cntThreads, entry.pcPriClassBase);
                } while (Native.Process32Next(snapshot, ref entry));
            }
            Native.CloseHandle(snapshot);
        }

        foreach (var (pid, info) in byPid)
        {
            if (pid == 0) continue;
            seen.Add(pid);
            var row = Processes.TryGetValue(pid, out var existing) ? existing : new ProcessRow { Pid = pid };
            row.Name = info.Name;
            row.ParentPid = info.ParentPid;
            row.ThreadCount = info.Threads;
            row.BasePriority = info.BasePriority;
            row.HasWindow = windowOwners.Contains((uint)pid);

            var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
            bool canQuery = h != IntPtr.Zero;
            ulong cpuTime = 0;
            try
            {
                if (string.IsNullOrEmpty(row.Path))
                {
                    uint size = 1024;
                    var sb = new System.Text.StringBuilder((int)size);
                    if (canQuery && Native.QueryFullProcessImageName(h, 0, sb, ref size)) row.Path = sb.ToString();
                }
                if (canQuery && Native.GetProcessTimes(h, out _, out _, out var kernel, out var user))
                {
                    cpuTime = kernel.Value + user.Value;
                    row.CpuTimeSeconds = cpuTime / 10_000_000.0;
                    if (_prevProcTime.TryGetValue(pid, out var previous) && cpuTime >= previous)
                    {
                        var delta = cpuTime - previous;
                        row.CpuPercent = Math.Clamp(100.0 * (delta / 10_000_000.0) / wallDelta / Environment.ProcessorCount, 0, 100 * Environment.ProcessorCount);
                    }
                    else row.CpuPercent = 0;
                    _prevProcTime[pid] = cpuTime;
                }
                var counters = new Native.PROCESS_MEMORY_COUNTERS_EX { cb = (uint)Marshal.SizeOf<Native.PROCESS_MEMORY_COUNTERS_EX>() };
                if (canQuery && Native.GetProcessMemoryInfo(h, ref counters, counters.cb))
                {
                    row.WorkingSetBytes = counters.WorkingSetSize;
                    row.PeakWorkingSetBytes = counters.PeakWorkingSetSize;
                    row.PrivateBytes = counters.PrivateUsage;
                    row.CommitBytes = counters.PagefileUsage;
                    row.PageFaults = counters.PageFaultCount;
                    row.PagedPoolBytes = counters.QuotaPagedPoolUsage;
                    row.NonPagedPoolBytes = counters.QuotaNonPagedPoolUsage;
                }
                if (canQuery && Native.GetProcessIoCounters(h, out var io))
                {
                    var totalIo = io.ReadTransferCount + io.WriteTransferCount + io.OtherTransferCount;
                    row.IoReadBytes = io.ReadTransferCount;
                    row.IoWriteBytes = io.WriteTransferCount;
                    row.IoOtherBytes = io.OtherTransferCount;
                    row.IoReadOps = io.ReadOperationCount;
                    row.IoWriteOps = io.WriteOperationCount;
                    row.IoOtherOps = io.OtherOperationCount;
                    if (_prevDisk.TryGetValue(pid, out var prevIo) && totalIo >= prevIo)
                        row.DiskBytesPerSec = (totalIo - prevIo) / wallDelta;
                    else row.DiskBytesPerSec = 0;
                    _prevDisk[pid] = totalIo;
                }
                if (canQuery && Native.GetProcessHandleCount(h, out var handles)) row.HandleCount = (int)handles;
                if (canQuery)
                {
                    var guiUser = Native.GetGuiResources(h, Native.GR_USEROBJECTS);
                    var guiGdi = Native.GetGuiResources(h, Native.GR_GDIOBJECTS);
                    row.UserObjects = (int)guiUser;
                    row.GdiObjects = (int)guiGdi;
                }
            }
            finally { if (canQuery) Native.CloseHandle(h); }

            if (string.IsNullOrEmpty(row.UserName))
            {
                row.UserName = Native.GetProcessUser((uint)pid, out var elevated);
                row.Elevated = elevated;
                row.IsPackaged = Win32Ext.IsPackaged((uint)pid);
            }
            if (row.StartTime == null)
            {
                try { row.StartTime = Process.GetProcessById(pid).StartTime; } catch { }
            }
            row.SessionId = Native.GetProcessSessionId((uint)pid);
            row.Platform = Native.Is32Bit((uint)pid) ? "32-bit" : "64-bit";
            row.Priority = Native.PriorityClassName(GetPriorityClassSafe(pid));

            _sampleTick++;
            if (_sampleTick % 4 == 0 || _sampleTick < 4)
                row.EfficiencyMode = ProcessActions.IsEfficiencyMode(pid);
            if (row.HasWindow) row.Status = Win32Ext.IsNotResponding((uint)pid) ? "Not responding" : "Running";
            else row.Status = row.EfficiencyMode ? "Efficiency mode" : "Running";

            row.Group = ClassifyProcess(row);
            row.MemHistory.Add(row.WorkingSetBytes / 1024.0 / 1024.0);
            row.CpuHistory.Add(row.CpuPercent);
            row.DiskHistory.Add(row.DiskBytesPerSec);
            row.GpuHistory.Add(row.GpuPercent);
            Processes[pid] = row;
        }

        foreach (var dead in Processes.Keys.Where(p => !seen.Contains(p)).ToList())
        {
            Processes.Remove(dead);
            _prevProcTime.Remove(dead);
            _prevDisk.Remove(dead);
        }

        ApplyGpuPerProcess();
        _ = totalCpuSeconds;
        _ = now;
    }

    private static uint GetPriorityClassSafe(int pid)
    {
        var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return 0;
        try { return Native.GetPriorityClass(h); }
        finally { Native.CloseHandle(h); }
    }

    private void ApplyGpuPerProcess()
    {
        if (!_gpuPdh.Collect()) return;
        var engines = _gpuPdh.GetArray("gpuEngine");
        var mem = _gpuPdh.GetArray("gpuProcMem");
        var memShared = _gpuPdh.GetArray("gpuProcMemShared");
        var perPid = new Dictionary<int, double>();
        foreach (var (instance, value) in engines)
        {
            var pid = ExtractPid(instance);
            if (pid <= 0) continue;
            perPid[pid] = perPid.GetValueOrDefault(pid) + value;
        }
        foreach (var row in Processes.Values)
        {
            row.HasGpu = perPid.TryGetValue(row.Pid, out var v) || row.GpuDedicatedBytes > 0;
            row.GpuPercent = Math.Clamp(perPid.GetValueOrDefault(row.Pid), 0, 100);
            var dedicated = mem.Where(m => ExtractPid(m.Instance) == row.Pid).Sum(m => m.Value);
            var shared = memShared.Where(m => ExtractPid(m.Instance) == row.Pid).Sum(m => m.Value);
            row.GpuDedicatedBytes = (ulong)Math.Max(0, dedicated);
            row.GpuSharedBytes = (ulong)Math.Max(0, shared);
            row.PowerUsage = EstimatePower(row.CpuPercent);
        }
    }

    internal static double? EstimatePower(double cpuPercent) => cpuPercent switch
    {
        < 0.5 => 1,      // Very low
        < 5 => 2,        // Low
        < 25 => 3,       // Moderate
        < 60 => 4,       // High
        _ => 5,          // Very high
    };

    internal static int ExtractPid(string instance)
    {
        if (!instance.StartsWith("pid_", StringComparison.OrdinalIgnoreCase)) return 0;
        var end = instance.IndexOf('_', 4);
        return end > 4 && int.TryParse(instance[4..end], out var pid) ? pid : 0;
    }

    internal static ProcessGroup ClassifyProcess(ProcessRow row)
    {
        if (row.HasWindow) return ProcessGroup.Apps;
        if (row.IsPackaged && !row.Path.Contains("SystemApps", StringComparison.OrdinalIgnoreCase)) return ProcessGroup.Apps;
        var path = row.Path;
        if (path.StartsWith(@"C:\Windows\", StringComparison.OrdinalIgnoreCase) &&
            !path.Contains(@"\WindowsApps\", StringComparison.OrdinalIgnoreCase))
            return ProcessGroup.Windows;
        return ProcessGroup.Background;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _pdh.Dispose();
        _gpuPdh.Dispose();
    }
}