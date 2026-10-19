using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TaskMgrClone.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;
    protected void Raise([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value; Raise(name); return true;
    }
}

/// <summary>Fixed capacity history used by every graph in the app.</summary>
public sealed class RingBuffer
{
    private readonly double[] _data;
    private int _count;
    private int _head;

    public RingBuffer(int capacity) { _data = new double[capacity]; Capacity = capacity; }
    public int Capacity { get; }
    public int Count => _count;

    public void Add(double value)
    {
        _data[_head] = value;
        _head = (_head + 1) % Capacity;
        if (_count < Capacity) _count++;
    }

    public void Clear() { _count = 0; _head = 0; Array.Clear(_data); }

    /// <summary>0 = oldest, Count-1 = newest.</summary>
    public double this[int index]
    {
        get
        {
            var start = (_head - _count + Capacity) % Capacity;
            return _data[(start + index) % Capacity];
        }
    }

    public double Max
    {
        get { double m = 0; for (int i = 0; i < _count; i++) m = Math.Max(m, this[i]); return m; }
    }
}

public enum ProcessGroup { Apps, Background, Windows }

public sealed class ProcessRow : Observable
{
    public required int Pid { get; init; }
    public int ParentPid { get; set; }
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string UserName { get; set; } = "";
    public string CommandLine { get; set; } = "";
    public DateTime? StartTime { get; set; }
    public ProcessGroup Group { get; set; }
    public int SessionId { get; set; }
    public bool Elevated { get; set; }
    public bool IsPackaged { get; set; }
    public string Platform { get; set; } = "";
    public string Priority { get; set; } = "";
    public int BasePriority { get; set; }
    public string Dep { get; set; } = "";
    public string Virtualization { get; set; } = "";
    public string Status { get; set; } = "";
    public int ThreadCount { get; set; }
    public int HandleCount { get; set; }
    public double CpuPercent { get; set; }
    public double CpuTimeSeconds { get; set; }
    public ulong WorkingSetBytes { get; set; }
    public ulong PrivateBytes { get; set; }
    public ulong CommitBytes { get; set; }
    public ulong PeakWorkingSetBytes { get; set; }
    public ulong PagedPoolBytes { get; set; }
    public ulong NonPagedPoolBytes { get; set; }
    public ulong PageFaults { get; set; }
    public ulong IoReadBytes { get; set; }
    public ulong IoWriteBytes { get; set; }
    public ulong IoOtherBytes { get; set; }
    public ulong IoReadOps { get; set; }
    public ulong IoWriteOps { get; set; }
    public ulong IoOtherOps { get; set; }
    public double DiskBytesPerSec { get; set; }
    public double? NetworkBytesPerSec { get; set; }
    public double GpuPercent { get; set; }
    public bool HasGpu { get; set; }
    public ulong GpuDedicatedBytes { get; set; }
    public ulong GpuSharedBytes { get; set; }
    public double? PowerUsage { get; set; }
    public double PowerUsageTrend { get; set; }
    public bool EfficiencyMode { get; set; }
    public bool Suspended { get; set; }
    public bool HasWindow { get; set; }
    public int UserObjects { get; set; }
    public int GdiObjects { get; set; }

    public RingBuffer CpuHistory { get; } = new(60);
    public RingBuffer MemHistory { get; } = new(60);
    public RingBuffer DiskHistory { get; } = new(60);
    public RingBuffer NetHistory { get; } = new(60);
    public RingBuffer GpuHistory { get; } = new(60);

    public override string ToString() => $"{Name} ({Pid})";
}

public sealed class ServiceRow : Observable
{
    public string Name { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Description { get; set; } = "";
    public string Group { get; set; } = "";
    public int Pid { get; set; }
    public string Status { get; set; } = "";
    public string StartupType { get; set; } = "";
    public bool Running => Status == "Running";
    public bool CanStop => Running;
    public bool CanStart => !Running;
}

public sealed class StartupRow : Observable
{
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Status { get; set; } = "Enabled";
    public string Impact { get; set; } = "Not measured";
    public string Command { get; set; } = "";
    public string ExecutablePath { get; set; } = "";
    public string RegistryKey { get; set; } = "";
    public string Identifier { get; set; } = "";
    public bool IsFolder { get; set; }
    public string Source { get; set; } = "";
}

public sealed class UserRow : Observable
{
    public uint SessionId { get; set; }
    public string UserName { get; set; } = "";
    public string Status { get; set; } = "";
    public string ClientName { get; set; } = "";
    public int ProcessCount { get; set; }
    public double CpuPercent { get; set; }
    public ulong MemoryBytes { get; set; }
    public double DiskBytesPerSec { get; set; }
    public double NetworkBytesPerSec { get; set; }
    public string SessionName { get; set; } = "";
}

public sealed class AppHistoryRow : Observable
{
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public TimeSpan CpuTime { get; set; }
    public double CpuPercentNow { get; set; }
    public ulong NetworkBytes { get; set; }
    public ulong MeteredBytes { get; set; }
    public ulong TileUpdates { get; set; }
    public ulong BackgroundCpuBytes { get; set; }
    public double BackgroundCpuPercent { get; set; }
    public ulong BackgroundNetworkBytes { get; set; }
    public ulong BackgroundMeteredBytes { get; set; }
    public string Status { get; set; } = "";
}

public sealed class GpuAdapterInfo
{
    public string Name { get; set; } = "";
    public string DriverVersion { get; set; } = "";
    public ulong DedicatedMemory { get; set; }
    public ulong SharedMemory { get; set; }
    public string Luid { get; set; } = "";
    public double Percent3D { get; set; }
    public double PercentCopy { get; set; }
    public double PercentVideoEncode { get; set; }
    public double PercentVideoDecode { get; set; }
    public double PercentCompute { get; set; }
    public double? Temperature { get; set; }
    public ulong DedicatedUsed { get; set; }
}
