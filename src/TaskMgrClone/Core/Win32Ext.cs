using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace TaskMgrClone.Core;

internal static class Win32Ext
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int max);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", SetLastError = true)] private static extern bool DestroyWindow(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr hwnd);

    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSDisconnectSession(IntPtr server, uint sessionId, bool wait);
    [DllImport("wtsapi32.dll", SetLastError = true)] private static extern bool WTSLogoffSession(IntPtr server, uint sessionId, bool wait);

    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool OpenProcessToken(IntPtr h, uint access, out IntPtr token);
    [DllImport("advapi32.dll", SetLastError = true)] private static extern bool GetTokenInformation(IntPtr token, int cls, IntPtr buf, uint len, out uint ret);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr ShellExecute(IntPtr hwnd, string? op, string? file, string? args, string? dir, int show);

    public const uint GW_OWNER = 4;
    public const uint WM_CLOSE = 0x0010;
    public const int TokenIsAppContainer = 29;

    /// <summary>Set of PIDs that own at least one visible top-level window.</summary>
    public static HashSet<uint> VisibleWindowOwners()
    {
        var owners = new HashSet<uint>();
        EnumWindows((hwnd, _) =>
        {
            if (IsWindowVisible(hwnd) && GetWindow(hwnd, GW_OWNER) == IntPtr.Zero && GetWindowTextLength(hwnd) > 0)
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                if (pid != 0) owners.Add(pid);
            }
            return true;
        }, IntPtr.Zero);
        return owners;
    }

    /// <summary>True when the process owns a visible window that stopped pumping messages.</summary>
    public static bool IsNotResponding(uint pid)
    {
        bool hung = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var p);
            if (p == pid && IsWindowVisible(hwnd) && IsHungAppWindow(hwnd)) hung = true;
            return true;
        }, IntPtr.Zero);
        return hung;
    }

    /// <summary>True when the process owns a visible top-level window (i.e. it is an app, not a background service).</summary>
    private static readonly Dictionary<int, string> DiskMediaCache = new();
    private const uint IOCTL_STORAGE_QUERY_PROPERTY = 0x2D1400;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(IntPtr handle, uint code, IntPtr inBuffer, int inSize,
        byte[] outBuffer, int outSize, out int returned, IntPtr overlapped);

    /// <summary>Vendor's disk subtitle, e.g. "SSD (NVMe)" / "HDD (SATA)", from storage descriptors.</summary>
    public static string GetDiskMediaType(int diskNumber)
    {
        lock (DiskMediaCache)
        {
            if (DiskMediaCache.TryGetValue(diskNumber, out var cached)) return cached;
        }
        string media = QueryDiskMediaType(diskNumber);
        lock (DiskMediaCache) { DiskMediaCache[diskNumber] = media; }
        return media;
    }

    private static string QueryDiskMediaType(int diskNumber)
    {
        IntPtr handle = Native.CreateFile(@"\\.\PhysicalDrive" + diskNumber, 0, 1 | 2, IntPtr.Zero, 3, 0, IntPtr.Zero);
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return "";
        try
        {
            var buffer = new byte[1024];
            bool ssd = false;
            IntPtr input = Marshal.AllocHGlobal(12);
            try
            {
                Marshal.WriteInt32(input, 0, 7);   // StorageDeviceSeekPenaltyProperty
                Marshal.WriteInt32(input, 4, 0);   // PropertyStandardQuery
                Marshal.WriteInt32(input, 8, 0);
                if (DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, input, 12, buffer, buffer.Length, out int bytes, IntPtr.Zero) && bytes >= 9)
                    ssd = buffer[8] == 0;
            }
            finally { Marshal.FreeHGlobal(input); }

            int bus = -1;
            input = Marshal.AllocHGlobal(12);
            try
            {
                Marshal.WriteInt32(input, 0, 0);   // StorageDeviceProperty
                Marshal.WriteInt32(input, 4, 0);
                Marshal.WriteInt32(input, 8, 0);
                if (DeviceIoControl(handle, IOCTL_STORAGE_QUERY_PROPERTY, input, 12, buffer, buffer.Length, out int bytes, IntPtr.Zero) && bytes >= 32)
                    bus = BitConverter.ToInt32(buffer, 28);
            }
            finally { Marshal.FreeHGlobal(input); }

            string busName = bus switch
            {
                1 => "SCSI", 3 => "ATA", 7 => "USB", 8 => "RAID", 10 => "SAS",
                11 => "SATA", 14 => "Virtual", 17 => "NVMe", _ => ""
            };
            if (bus == 17) return "SSD (NVMe)";
            if (ssd && busName.Length > 0) return "SSD (" + busName + ")";
            if (ssd) return "SSD";
            if (busName.Length > 0) return "HDD (" + busName + ")";
            return "";
        }
        finally { Native.CloseHandle(handle); }
    }

    public static bool HasVisibleWindow(uint pid)
    {
        bool found = false;
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var p);
            if (p == pid && IsWindowVisible(hwnd) && GetWindowTextLength(hwnd) > 0) found = true;
            return !found;
        }, IntPtr.Zero);
        return found;
    }

    public static void CloseMainWindow(uint pid)
    {
        EnumWindows((hwnd, _) =>
        {
            GetWindowThreadProcessId(hwnd, out var p);
            if (p == pid && IsWindowVisible(hwnd) && GetWindowTextLength(hwnd) > 0)
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
    }

    public static bool IsPackaged(uint pid)
    {
        var h = Native.OpenProcess(Native.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(h, 0x0008, out var token)) return false;
            try
            {
                var buf = Marshal.AllocHGlobal(4);
                try { return GetTokenInformation(token, TokenIsAppContainer, buf, 4, out _) && Marshal.ReadInt32(buf) != 0; }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { Native.CloseHandle(token); }
        }
        finally { Native.CloseHandle(h); }
    }

    public static bool DisconnectSession(uint sessionId) => WTSDisconnectSession(IntPtr.Zero, sessionId, false);
    public static bool LogoffSession(uint sessionId) => WTSLogoffSession(IntPtr.Zero, sessionId, false);

    public static void ShellOpen(string target) => ShellExecute(IntPtr.Zero, "open", target, null, null, 1);

    public static void ShowProperties(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists) { ShellOpen(path); return; }
            var dir = info.DirectoryName ?? "";
            // ShellExecuteEx with the documented verb shows the properties sheet.
            var psi = new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true };
            Process.Start(psi);
        }
        catch { /* best effort */ }
    }

    public static bool RunTask(string command, bool elevated)
    {
        try
        {
            var psi = new ProcessStartInfo(elevated ? "cmd.exe" : command)
            {
                UseShellExecute = true,
            };
            if (elevated) { psi.Arguments = "/k " + command; psi.Verb = "runas"; }
            Process.Start(psi);
            return true;
        }
        catch { return false; }
    }
}

internal static class ProcessActions
{
    public static bool TryParsePriority(string text, out uint cls)
    {
        cls = text switch
        {
            "Realtime" => Native.REALTIME_PRIORITY_CLASS,
            "High" => Native.HIGH_PRIORITY_CLASS,
            "Above normal" => Native.ABOVE_NORMAL_PRIORITY_CLASS,
            "Normal" => Native.NORMAL_PRIORITY_CLASS,
            "Below normal" => Native.BELOW_NORMAL_PRIORITY_CLASS,
            "Low" => Native.IDLE_PRIORITY_CLASS,
            _ => 0,
        };
        return cls != 0;
    }

    /// <summary>Task Manager's End task: ask windowed apps to close, then terminate if they do not.</summary>
    public static bool EndTaskGraceful(int pid)
    {
        if (pid <= 4) return false;
        if (Win32Ext.HasVisibleWindow((uint)pid))
        {
            Win32Ext.CloseMainWindow((uint)pid);
            var clock = Stopwatch.StartNew();
            while (clock.ElapsedMilliseconds < 1500)
            {
                if (!IsAlive(pid)) return true;
                Thread.Sleep(80);
            }
            if (!IsAlive(pid)) return true;
        }
        return Kill(pid);
    }

    private static bool IsAlive(int pid)
    {
        try { using var process = Process.GetProcessById(pid); return !process.HasExited; }
        catch { return false; }
    }

    public static bool EndTask(int pid, bool tree)
    {
        var ok = Kill(pid);
        if (tree)
        {
            foreach (var child in Children(pid))
                EndTask(child, true);
        }
        return ok;
    }

    public static bool Kill(int pid)
    {
        var h = Native.OpenProcess(Native.PROCESS_TERMINATE | Native.PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return false;
        try { return Native.TerminateProcess(h, 1); }
        finally { Native.CloseHandle(h); }
    }

    public static IEnumerable<int> Children(int pid)
    {
        var result = new List<int>();
        var snapshot = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
        if (snapshot == IntPtr.Zero) return result;
        try
        {
            var entry = new Native.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<Native.PROCESSENTRY32>() };
            if (Native.Process32First(snapshot, ref entry))
            {
                do
                {
                    if ((int)entry.th32ParentProcessID == pid) result.Add((int)entry.th32ProcessID);
                } while (Native.Process32Next(snapshot, ref entry));
            }
        }
        finally { Native.CloseHandle(snapshot); }
        return result;
    }

    public static bool SetPriority(int pid, uint cls)
    {
        var h = Native.OpenProcess(Native.PROCESS_SET_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return false;
        try { return Native.SetPriorityClass(h, cls); }
        finally { Native.CloseHandle(h); }
    }

    public static bool SetEfficiencyMode(int pid, bool enabled)
    {
        // ProcessPowerThrottling: PROCESS_POWER_THROTTLING_STATE { Version=1, ControlMask, StateMask }
        const int ProcessPowerThrottling = 4;
        const uint PROCESS_POWER_THROTTLING_CURRENT_VERSION = 1;
        const uint PROCESS_POWER_THROTTLING_EXECUTION_SPEED = 0x1;
        var state = new byte[12];
        BitConverter.GetBytes(PROCESS_POWER_THROTTLING_CURRENT_VERSION).CopyTo(state, 0);
        BitConverter.GetBytes(PROCESS_POWER_THROTTLING_EXECUTION_SPEED).CopyTo(state, 4);
        BitConverter.GetBytes(enabled ? PROCESS_POWER_THROTTLING_EXECUTION_SPEED : 0u).CopyTo(state, 8);

        var h = Native.OpenProcess(Native.PROCESS_SET_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            var handle = GCHandle.Alloc(state, GCHandleType.Pinned);
            try { return SetProcessInformation(h, ProcessPowerThrottling, handle.AddrOfPinnedObject(), state.Length); }
            finally { handle.Free(); }
        }
        finally { Native.CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetProcessInformation(IntPtr h, int infoClass, IntPtr info, int size);

    /// <summary>Reads PROCESS_POWER_THROTTLING_STATE to detect EcoQoS ("Efficiency mode").</summary>
    public static bool IsEfficiencyMode(int pid)
    {
        const int ProcessPowerThrottling = 4;
        const uint ExecutionSpeedMask = 0x1;
        foreach (var access in new[] { Native.PROCESS_QUERY_LIMITED_INFORMATION, Native.PROCESS_QUERY_INFORMATION })
        {
            var h = Native.OpenProcess(access, false, (uint)pid);
            if (h == IntPtr.Zero) continue;
            try
            {
                var state = new byte[12];
                var pinned = GCHandle.Alloc(state, GCHandleType.Pinned);
                try
                {
                    if (GetProcessInformation(h, ProcessPowerThrottling, pinned.AddrOfPinnedObject(), state.Length))
                        return (BitConverter.ToUInt32(state, 8) & ExecutionSpeedMask) != 0;
                }
                finally { pinned.Free(); }
            }
            finally { Native.CloseHandle(h); }
        }
        return false;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool SetProcessInformation(IntPtr h, int infoClass, IntPtr info, int size);

    public static bool Restart(int pid)
    {
        var path = Native.GetProcessImagePath((uint)pid);
        if (string.IsNullOrEmpty(path)) return false;
        var args = Native.GetProcessCommandLine((uint)pid);
        if (string.IsNullOrWhiteSpace(args)) args = string.Empty;
        else
        {
            var idx = args.IndexOf(path, StringComparison.OrdinalIgnoreCase);
            args = idx >= 0 ? args[(idx + path.Length)..].Trim() : string.Empty;
        }
        try
        {
            if (!Kill(pid)) return false;
            Process.Start(new ProcessStartInfo(path, args) { UseShellExecute = true });
            return true;
        }
        catch { return false; }
    }

    public static bool CreateDump(int pid, string outputPath)
    {
        var h = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION | Native.PROCESS_VM_READ, false, (uint)pid);
        if (h == IntPtr.Zero) return false;
        var file = Native.CreateFile(outputPath, Native.GENERIC_WRITE, 0, IntPtr.Zero, Native.CREATE_ALWAYS, Native.FILE_ATTRIBUTE_NORMAL, IntPtr.Zero);
        if (file == IntPtr.Zero || file == new IntPtr(-1)) { Native.CloseHandle(h); return false; }
        try
        {
            const uint type = Native.MINIDUMP_NORMAL | Native.MINIDUMP_WITH_DATA_SEGS | Native.MINIDUMP_WITH_FULL_MEMORY | Native.MINIDUMP_WITH_HANDLE_DATA;
            return Native.MiniDumpWriteDump(h, (uint)pid, file, type, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
        }
        finally { Native.CloseHandle(file); Native.CloseHandle(h); }
    }

    public static (long ProcessMask, long SystemMask) GetAffinity(int pid)
    {
        var h = Native.OpenProcess(Native.PROCESS_QUERY_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return (0, 0);
        try { return Native.GetProcessAffinityMask(h, out var p, out var s) ? ((long)p, (long)s) : (0, 0); }
        finally { Native.CloseHandle(h); }
    }

    public static bool SetAffinity(int pid, long mask)
    {
        var h = Native.OpenProcess(Native.PROCESS_SET_INFORMATION | Native.PROCESS_QUERY_INFORMATION, false, (uint)pid);
        if (h == IntPtr.Zero) return false;
        try { return Native.SetProcessAffinityMask(h, (IntPtr)mask); }
        finally { Native.CloseHandle(h); }
    }
}