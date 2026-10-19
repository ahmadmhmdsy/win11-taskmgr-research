using System.Runtime.InteropServices;
using System.Text;

namespace TaskMgrClone.Core;

internal static class Native
{
    // ---------- constants ----------
    public const uint PROCESS_TERMINATE = 0x0001;
    public const uint PROCESS_SET_INFORMATION = 0x0200;
    public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
    public const uint PROCESS_QUERY_INFORMATION = 0x0400;
    public const uint PROCESS_VM_READ = 0x0010;
    public const uint SYNCHRONIZE = 0x00100000;

    public const uint TH32CS_SNAPPROCESS = 0x00000002;

    public const int ProcessBasicInformation = 0;

    public const int TokenUser = 1;
    public const int TokenElevation = 20;
    public const int TokenElevationType = 18;

    public const int ProcessDEPPolicy = 0;
    public const int ProcessASLRPolicy = 1;

    public const uint GR_GDIOBJECTS = 0;
    public const uint GR_USEROBJECTS = 1;

    public const uint SC_MANAGER_CONNECT = 0x0001;
    public const uint SC_MANAGER_ENUMERATE_SERVICE = 0x0004;
    public const uint SERVICE_QUERY_STATUS = 0x0004;
    public const uint SERVICE_START = 0x0010;
    public const uint SERVICE_STOP = 0x0020;
    public const uint SERVICE_PAUSE_CONTINUE = 0x0040;
    public const uint SERVICE_CHANGE_CONFIG = 0x0002;
    public const uint SERVICE_QUERY_CONFIG = 0x0001;

    public const int SC_ENUM_PROCESS_INFO = 0;
    public const uint SERVICE_CONTROL_STOP = 0x00000001;
    public const uint SERVICE_CONTROL_CONTINUE = 0x00000003;
    public const uint SERVICE_ACTIVE = 0x00000001;
    public const uint SERVICE_INACTIVE = 0x00000002;
    public const uint SERVICE_STATE_ALL = SERVICE_ACTIVE | SERVICE_INACTIVE;

    public const int WTSUserName = 5;
    public const int WTSDomainName = 7;
    public const int WTSConnectState = 8;
    public const int WTSClientName = 10;
    public const int WTSSessionId = 4;
    public const int WTSWinStationName = 6;

    public const uint CREATE_ALWAYS = 2;
    public const uint GENERIC_WRITE = 0x40000000;
    public const uint FILE_ATTRIBUTE_NORMAL = 0x00000080;

    public const uint MINIDUMP_NORMAL = 0x00000000;
    public const uint MINIDUMP_WITH_DATA_SEGS = 0x00000001;
    public const uint MINIDUMP_WITH_FULL_MEMORY = 0x00000002;
    public const uint MINIDUMP_WITH_HANDLE_DATA = 0x00000004;

    // priority classes
    public const uint IDLE_PRIORITY_CLASS = 0x00000040;
    public const uint BELOW_NORMAL_PRIORITY_CLASS = 0x00004000;
    public const uint NORMAL_PRIORITY_CLASS = 0x00000020;
    public const uint ABOVE_NORMAL_PRIORITY_CLASS = 0x00008000;
    public const uint HIGH_PRIORITY_CLASS = 0x00000080;
    public const uint REALTIME_PRIORITY_CLASS = 0x00000100;

    // ---------- structs ----------
    [StructLayout(LayoutKind.Sequential)]
    public struct FILETIME { public uint dwLowDateTime; public uint dwHighDateTime; public ulong Value => ((ulong)dwHighDateTime << 32) | dwLowDateTime; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_MEMORY_COUNTERS_EX
    {
        public uint cb;
        public uint PageFaultCount;
        public nuint PeakWorkingSetSize;
        public nuint WorkingSetSize;
        public nuint QuotaPeakPagedPoolUsage;
        public nuint QuotaPagedPoolUsage;
        public nuint QuotaPeakNonPagedPoolUsage;
        public nuint QuotaNonPagedPoolUsage;
        public nuint PagefileUsage;
        public nuint PeakPagefileUsage;
        public nuint PrivateUsage;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PERFORMANCE_INFORMATION
    {
        public uint cb;
        public nuint CommitTotal;
        public nuint CommitLimit;
        public nuint CommitPeak;
        public nuint PhysicalTotal;
        public nuint PhysicalAvailable;
        public nuint SystemCache;
        public nuint KernelTotal;
        public nuint KernelPaged;
        public nuint KernelNonPaged;
        public nuint PageSize;
        public uint HandleCount;
        public uint ProcessCount;
        public uint ThreadCount;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct PROCESSENTRY32
    {
        public uint dwSize;
        public uint cntUsage;
        public uint th32ProcessID;
        public IntPtr th32DefaultHeapID;
        public uint th32ModuleID;
        public uint cntThreads;
        public uint th32ParentProcessID;
        public int pcPriClassBase;
        public uint dwFlags;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExeFile;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PROCESS_BASIC_INFORMATION
    {
        public IntPtr Reserved1;
        public IntPtr PebBaseAddress;
        public IntPtr Reserved2a;
        public IntPtr Reserved2b;
        public IntPtr UniqueProcessId;
        public IntPtr Reserved3;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct UNICODE_STRING
    {
        public ushort Length;
        public ushort MaximumLength;
        public IntPtr Buffer;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct TOKEN_ELEVATION { public int TokenIsElevated; }

    [StructLayout(LayoutKind.Sequential)]
    public struct SERVICE_STATUS_PROCESS
    {
        public uint dwServiceType;
        public uint dwCurrentState;
        public uint dwControlsAccepted;
        public uint dwWin32ExitCode;
        public uint dwServiceSpecificExitCode;
        public uint dwCheckPoint;
        public uint dwWaitHint;
        public uint dwProcessId;
        public uint dwServiceFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct ENUM_SERVICE_STATUS_PROCESS
    {
        public IntPtr lpServiceName;
        public IntPtr lpDisplayName;
        public SERVICE_STATUS_PROCESS ServiceStatusProcess;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct WTS_SESSION_INFO
    {
        public uint SessionID;
        public IntPtr pWinStationName;
        public int State;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct QUERY_SERVICE_CONFIG
    {
        public uint dwServiceType;
        public uint dwStartType;
        public uint dwErrorControl;
        public IntPtr lpBinaryPathName;
        public IntPtr lpLoadOrderGroup;
        public uint dwTagId;
        public IntPtr lpDependencies;
        public IntPtr lpServiceStartName;
        public IntPtr lpDisplayName;
    }

    // ---------- kernel32 / user32 / advapi32 ----------
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool EnumProcesses(uint[] lpidProcess, uint cb, out uint lpcbNeeded);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr OpenProcess(uint access, bool inheritHandle, uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessTimes(IntPtr h, out FILETIME creation, out FILETIME exit, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessIoCounters(IntPtr h, out IO_COUNTERS counters);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessHandleCount(IntPtr h, out uint count);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern uint GetPriorityClass(IntPtr h);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetPriorityClass(IntPtr h, uint priorityClass);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessAffinityMask(IntPtr h, out IntPtr processAffinity, out IntPtr systemAffinity);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool SetProcessAffinityMask(IntPtr h, IntPtr mask);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool TerminateProcess(IntPtr h, uint exitCode);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryFullProcessImageName(IntPtr h, uint flags, StringBuilder name, ref uint size);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetSystemTimes(out FILETIME idle, out FILETIME kernel, out FILETIME user);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool GetPerformanceInfo(out PERFORMANCE_INFORMATION info, uint cb);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint processId);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool Process32First(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool Process32Next(IntPtr snapshot, ref PROCESSENTRY32 entry);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessWorkingSetSizeEx(IntPtr h, out nuint min, out nuint max, out uint flags);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool IsWow64Process2(IntPtr h, out ushort processMachine, out ushort nativeMachine);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetLogicalProcessorInformationEx(int relationshipType, IntPtr buffer, ref uint returnedLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool GetProcessMitigationPolicy(IntPtr h, int policy, ref uint buffer, nuint size);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr CreateFile(string name, uint access, uint share, IntPtr sa, uint disposition, uint flags, IntPtr template);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern uint GetGuiResources(IntPtr h, uint flags);

    [DllImport("psapi.dll", SetLastError = true)]
    public static extern bool GetProcessMemoryInfo(IntPtr h, ref PROCESS_MEMORY_COUNTERS_EX counters, uint size);

    [DllImport("ntdll.dll")]
    public static extern int NtQueryInformationProcess(IntPtr h, int infoClass, ref PROCESS_BASIC_INFORMATION info, int size, out int returnLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool ReadProcessMemory(IntPtr h, IntPtr baseAddress, byte[] buffer, nuint size, out nuint read);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool OpenProcessToken(IntPtr h, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool GetTokenInformation(IntPtr token, int infoClass, IntPtr buffer, uint length, out uint returnLength);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool LookupAccountSid(string? systemName, IntPtr sid, StringBuilder name, ref uint nameLen, StringBuilder domain, ref uint domainLen, out int use);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenSCManager(string? machine, string? database, uint access);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool EnumServicesStatusEx(IntPtr scm, int infoLevel, uint serviceType, uint serviceState, IntPtr buffer, uint bufferSize, out uint bytesNeeded, out uint servicesReturned, IntPtr resumeHandle, string? groupName);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr OpenService(IntPtr scm, string name, uint access);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool QueryServiceStatusEx(IntPtr service, int infoLevel, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool StartService(IntPtr service, uint numArgs, string[]? args);

    [DllImport("advapi32.dll", SetLastError = true)]
    public static extern bool ControlService(IntPtr service, uint control, ref SERVICE_STATUS_PROCESS status);

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool QueryServiceConfig(IntPtr service, IntPtr config, uint bufferSize, out uint bytesNeeded);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool WTSEnumerateSessions(IntPtr server, uint reserved, uint version, ref IntPtr sessions, ref uint count);

    [DllImport("wtsapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern bool WTSQuerySessionInformation(IntPtr server, uint sessionId, int infoClass, out IntPtr buffer, out uint bytesReturned);

    [DllImport("wtsapi32.dll")]
    public static extern void WTSFreeMemory(IntPtr memory);

    [DllImport("dbghelp.dll", SetLastError = true)]
    public static extern bool MiniDumpWriteDump(IntPtr process, uint processId, IntPtr file, uint dumpType, IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);

    // ---------- helpers ----------
    public static int GetProcessSessionId(uint pid) => ProcessIdToSessionId(pid, out var session) ? (int)session : 0;

    public static string GetProcessImagePath(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return string.Empty;
        try
        {
            uint size = 1024;
            var sb = new StringBuilder((int)size);
            return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString() : string.Empty;
        }
        finally { CloseHandle(h); }
    }

    public static string GetProcessUser(uint pid, out bool elevated)
    {
        elevated = false;
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return string.Empty;
        try
        {
            if (!OpenProcessToken(h, 0x0008 /*TOKEN_QUERY*/, out var token)) return string.Empty;
            try
            {
                uint len = 0;
                GetTokenInformation(token, TokenElevation, IntPtr.Zero, 0, out len);
                var eBuf = Marshal.AllocHGlobal((int)len);
                try
                {
                    if (GetTokenInformation(token, TokenElevation, eBuf, len, out _))
                        elevated = Marshal.ReadInt32(eBuf) != 0;
                }
                finally { Marshal.FreeHGlobal(eBuf); }

                GetTokenInformation(token, TokenUser, IntPtr.Zero, 0, out len);
                if (len == 0) return string.Empty;
                var buf = Marshal.AllocHGlobal((int)len);
                try
                {
                    if (!GetTokenInformation(token, TokenUser, buf, len, out _)) return string.Empty;
                    var sid = Marshal.ReadIntPtr(buf);
                    var name = new StringBuilder(256);
                    var domain = new StringBuilder(256);
                    uint nameLen = 256, domainLen = 256;
                    if (!LookupAccountSid(null, sid, name, ref nameLen, domain, ref domainLen, out _)) return string.Empty;
                    var d = domain.ToString();
                    return string.IsNullOrEmpty(d) ? name.ToString() : d + "\\" + name.ToString();
                }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(h); }
    }

    public static string GetProcessCommandLine(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_INFORMATION | PROCESS_VM_READ, false, pid);
        if (h == IntPtr.Zero) return string.Empty;
        try
        {
            var pbi = new PROCESS_BASIC_INFORMATION();
            if (NtQueryInformationProcess(h, ProcessBasicInformation, ref pbi, Marshal.SizeOf<PROCESS_BASIC_INFORMATION>(), out _) != 0) return string.Empty;
            // PEB->ProcessParameters at offset 0x20 (x64)
            if (!ReadPtr(h, pbi.PebBaseAddress + 0x20, out var parameters) || parameters == IntPtr.Zero) return string.Empty;
            // RTL_USER_PROCESS_PARAMETERS->CommandLine UNICODE_STRING at offset 0x70 (x64)
            var us = ReadStruct<UNICODE_STRING>(h, parameters + 0x70);
            if (us.Length == 0 || us.Buffer == IntPtr.Zero) return string.Empty;
            var bytes = new byte[us.Length];
            if (!ReadProcessMemory(h, us.Buffer, bytes, (nuint)bytes.Length, out _)) return string.Empty;
            return Encoding.Unicode.GetString(bytes);
        }
        catch { return string.Empty; }
        finally { CloseHandle(h); }
    }

    private static bool ReadPtr(IntPtr h, IntPtr address, out IntPtr value)
    {
        var buf = new byte[IntPtr.Size];
        if (!ReadProcessMemory(h, address, buf, (nuint)buf.Length, out _)) { value = IntPtr.Zero; return false; }
        value = (IntPtr)BitConverter.ToInt64(buf, 0);
        return true;
    }

    private static T ReadStruct<T>(IntPtr h, IntPtr address) where T : struct
    {
        var size = Marshal.SizeOf<T>();
        var buf = new byte[size];
        if (!ReadProcessMemory(h, address, buf, (nuint)size, out _)) return default;
        var gch = GCHandle.Alloc(buf, GCHandleType.Pinned);
        try { return Marshal.PtrToStructure<T>(gch.AddrOfPinnedObject()); }
        finally { gch.Free(); }
    }

    // Windows 32bit check (any WOW64 process)
    public static bool Is32Bit(uint pid)
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (h == IntPtr.Zero) return false;
        try
        {
            if (IsWow64Process2(h, out var machine, out _))
                return machine != 0; // processMachine != IMAGE_FILE_MACHINE_UNKNOWN -> emulated
            return false;
        }
        finally { CloseHandle(h); }
    }

    public static string PriorityClassName(uint cls) => cls switch
    {
        IDLE_PRIORITY_CLASS => "Low",
        BELOW_NORMAL_PRIORITY_CLASS => "Below normal",
        NORMAL_PRIORITY_CLASS => "Normal",
        ABOVE_NORMAL_PRIORITY_CLASS => "Above normal",
        HIGH_PRIORITY_CLASS => "High",
        REALTIME_PRIORITY_CLASS => "Realtime",
        _ => "",
    };

    public static bool IsElevatedSelf()
    {
        var h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, (uint)Environment.ProcessId);
        if (h == IntPtr.Zero) return false;
        try
        {
            if (!OpenProcessToken(h, 0x0008, out var token)) return false;
            try
            {
                var buf = Marshal.AllocHGlobal(4);
                try { return GetTokenInformation(token, TokenElevation, buf, 4, out _) && Marshal.ReadInt32(buf) != 0; }
                finally { Marshal.FreeHGlobal(buf); }
            }
            finally { CloseHandle(token); }
        }
        finally { CloseHandle(h); }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(uint processId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    public static void AttachConsoleToParent()
    {
        const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
        if (!AttachConsole(ATTACH_PARENT_PROCESS)) AllocConsole();
    }

    public static int PhysicalCoreCount()
    {
        uint len = 0;
        const int RelationProcessorCore = 0;
        GetLogicalProcessorInformationEx(RelationProcessorCore, IntPtr.Zero, ref len);
        if (len == 0) return Environment.ProcessorCount;
        var buf = Marshal.AllocHGlobal((int)len);
        try
        {
            if (!GetLogicalProcessorInformationEx(RelationProcessorCore, buf, ref len)) return Environment.ProcessorCount;
            int count = 0;
            int offset = 0;
            while (offset < len)
            {
                var relationship = Marshal.ReadInt32(buf + offset);
                var size = Marshal.ReadInt32(buf + offset + 4);
                if (relationship == RelationProcessorCore) count++;
                if (size <= 0) break;
                offset += size;
            }
            return count == 0 ? Environment.ProcessorCount : count;
        }
        finally { Marshal.FreeHGlobal(buf); }
    }
}