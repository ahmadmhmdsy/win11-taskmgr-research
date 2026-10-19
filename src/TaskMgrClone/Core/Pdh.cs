using System.Runtime.InteropServices;

namespace TaskMgrClone.Core;

/// <summary>
/// Minimal PDH wrapper: wildcard counters, per-instance formatted values.
/// </summary>
internal sealed class PdhQuery : IDisposable
{
    private const uint PDH_FMT_DOUBLE = 0x00000200;
    private const uint PDH_FMT_NOCAP100 = 0x00008000;
    private const uint PDH_MORE_DATA = 0x800007D2;
    private const uint PDH_CSTATUS_VALID_DATA = 0x00000000;
    private const uint PDH_CSTATUS_NEW_DATA = 0x00000001;

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE { public uint CStatus; public double doubleValue; }

    [StructLayout(LayoutKind.Sequential)]
    private struct PDH_FMT_COUNTERVALUE_ITEM { public IntPtr szName; public PDH_FMT_COUNTERVALUE FmtValue; }

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhOpenQuery(string? dataSource, IntPtr userData, out IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhAddEnglishCounter(IntPtr query, string path, IntPtr userData, out IntPtr counter);

    [DllImport("pdh.dll")]
    private static extern uint PdhCollectQueryData(IntPtr query);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterArray(IntPtr counter, uint format, ref uint bufferSize, ref uint itemCount, IntPtr buffer);

    [DllImport("pdh.dll", CharSet = CharSet.Unicode)]
    private static extern uint PdhGetFormattedCounterValue(IntPtr counter, uint format, out uint type, out PDH_FMT_COUNTERVALUE value);

    [DllImport("pdh.dll")]
    private static extern uint PdhCloseQuery(IntPtr query);

    private IntPtr _query;
    private readonly Dictionary<string, IntPtr> _counters = new();
    public bool Available { get; private set; }

    public PdhQuery()
    {
        Available = PdhOpenQuery(null, IntPtr.Zero, out _query) == 0;
    }

    public bool AddCounter(string key, string path)
    {
        if (!Available) return false;
        var status = PdhAddEnglishCounter(_query, path, IntPtr.Zero, out var counter);
        if (status != 0) return false;
        _counters[key] = counter;
        return true;
    }

    public bool Collect() => Available && PdhCollectQueryData(_query) == 0;

    public List<(string Instance, double Value)> GetArray(string key)
    {
        var result = new List<(string, double)>();
        if (!Available || !_counters.TryGetValue(key, out var counter)) return result;

        uint size = 0, count = 0;
        var status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, ref count, IntPtr.Zero);
        if (status != PDH_MORE_DATA && status != 0) return result;
        if (size == 0) return result;

        var buffer = Marshal.AllocHGlobal((int)size);
        try
        {
            status = PdhGetFormattedCounterArray(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, ref size, ref count, buffer);
            if (status != 0) return result;
            var itemSize = Marshal.SizeOf<PDH_FMT_COUNTERVALUE_ITEM>();
            for (int i = 0; i < count; i++)
            {
                var item = Marshal.PtrToStructure<PDH_FMT_COUNTERVALUE_ITEM>(buffer + i * itemSize);
                var name = Marshal.PtrToStringUni(item.szName) ?? string.Empty;
                if (item.FmtValue.CStatus != PDH_CSTATUS_VALID_DATA && item.FmtValue.CStatus != PDH_CSTATUS_NEW_DATA) continue;
                result.Add((name, item.FmtValue.doubleValue));
            }
        }
        finally { Marshal.FreeHGlobal(buffer); }
        return result;
    }

    public double GetSingle(string key)
    {
        if (!Available || !_counters.TryGetValue(key, out var counter)) return 0;
        return PdhGetFormattedCounterValue(counter, PDH_FMT_DOUBLE | PDH_FMT_NOCAP100, out _, out var value) == 0 ? value.doubleValue : 0;
    }

    public void Dispose()
    {
        if (_query != IntPtr.Zero) { PdhCloseQuery(_query); _query = IntPtr.Zero; }
        Available = false;
    }
}
