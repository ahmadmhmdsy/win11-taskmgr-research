using System.Runtime.InteropServices;

namespace TaskMgrClone.Core;

internal static class ServiceControl
{
    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool QueryServiceConfig2(IntPtr service, uint infoLevel, IntPtr buffer, uint bufferSize, out uint bytesNeeded);

    [StructLayout(LayoutKind.Sequential)]
    private struct SERVICE_DESCRIPTION { public IntPtr lpDescription; }

    public static List<ServiceRow> Enumerate()
    {
        var rows = new List<ServiceRow>();
        var scm = Native.OpenSCManager(null, null, Native.SC_MANAGER_CONNECT | Native.SC_MANAGER_ENUMERATE_SERVICE);
        if (scm == IntPtr.Zero) return rows;
        try
        {
            uint bytesNeeded = 0, count = 0;
            Native.EnumServicesStatusEx(scm, Native.SC_ENUM_PROCESS_INFO, 0x00000030 /*WIN32|ADAPTER*/, Native.SERVICE_STATE_ALL, IntPtr.Zero, 0, out bytesNeeded, out count, IntPtr.Zero, null);
            var buffer = Marshal.AllocHGlobal((int)bytesNeeded);
            try
            {
                if (!Native.EnumServicesStatusEx(scm, Native.SC_ENUM_PROCESS_INFO, 0x00000030, Native.SERVICE_STATE_ALL, buffer, bytesNeeded, out _, out count, IntPtr.Zero, null))
                    return rows;
                var itemSize = Marshal.SizeOf<Native.ENUM_SERVICE_STATUS_PROCESS>();
                for (int i = 0; i < count; i++)
                {
                    var item = Marshal.PtrToStructure<Native.ENUM_SERVICE_STATUS_PROCESS>(buffer + i * itemSize);
                    var name = Marshal.PtrToStringUni(item.lpServiceName) ?? "";
                    rows.Add(new ServiceRow
                    {
                        Name = name,
                        DisplayName = Marshal.PtrToStringUni(item.lpDisplayName) ?? name,
                        Pid = (int)item.ServiceStatusProcess.dwProcessId,
                        Status = DescribeState(item.ServiceStatusProcess.dwCurrentState),
                    });
                }
            }
            finally { Marshal.FreeHGlobal(buffer); }

            foreach (var row in rows)
            {
                var svc = Native.OpenService(scm, row.Name, Native.SERVICE_QUERY_CONFIG | Native.SERVICE_QUERY_STATUS);
                if (svc == IntPtr.Zero) continue;
                try
                {
                    uint needed = 0;
                    Native.QueryServiceConfig(svc, IntPtr.Zero, 0, out needed);
                    var cfgBuf = Marshal.AllocHGlobal((int)needed);
                    try
                    {
                        if (Native.QueryServiceConfig(svc, cfgBuf, needed, out _))
                        {
                            var cfg = Marshal.PtrToStructure<Native.QUERY_SERVICE_CONFIG>(cfgBuf);
                            row.StartupType = DescribeStartType(cfg.dwStartType);
                            row.Group = Marshal.PtrToStringUni(cfg.lpLoadOrderGroup) ?? "";
                        }
                    }
                    finally { Marshal.FreeHGlobal(cfgBuf); }

                    uint descNeeded = 0;
                    QueryServiceConfig2(svc, 1 /*DESCRIPTION*/, IntPtr.Zero, 0, out descNeeded);
                    if (descNeeded > 0)
                    {
                        var descBuf = Marshal.AllocHGlobal((int)descNeeded);
                        try
                        {
                            if (QueryServiceConfig2(svc, 1, descBuf, descNeeded, out _))
                            {
                                var desc = Marshal.PtrToStructure<SERVICE_DESCRIPTION>(descBuf);
                                row.Description = Marshal.PtrToStringUni(desc.lpDescription) ?? "";
                            }
                        }
                        finally { Marshal.FreeHGlobal(descBuf); }
                    }
                }
                finally { Native.CloseHandle(svc); }
            }
        }
        finally { Native.CloseHandle(scm); }
        return rows.OrderBy(r => r.DisplayName, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    public static bool Start(string name)
    {
        var scm = Native.OpenSCManager(null, null, Native.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var svc = Native.OpenService(scm, name, Native.SERVICE_START | Native.SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;
            try { return Native.StartService(svc, 0, null); }
            finally { Native.CloseHandle(svc); }
        }
        finally { Native.CloseHandle(scm); }
    }

    public static bool Stop(string name)
    {
        var scm = Native.OpenSCManager(null, null, Native.SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return false;
        try
        {
            var svc = Native.OpenService(scm, name, Native.SERVICE_STOP | Native.SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return false;
            try
            {
                var status = new Native.SERVICE_STATUS_PROCESS();
                return Native.ControlService(svc, Native.SERVICE_CONTROL_STOP, ref status);
            }
            finally { Native.CloseHandle(svc); }
        }
        finally { Native.CloseHandle(scm); }
    }

    public static bool Restart(string name) => Stop(name) && Start(name);

    private static string DescribeState(uint state) => state switch
    {
        1 => "Stopped",
        2 => "Start pending",
        3 => "Stop pending",
        4 => "Running",
        5 => "Continue pending",
        6 => "Pause pending",
        7 => "Paused",
        _ => "Unknown",
    };

    private static string DescribeStartType(uint type) => type switch
    {
        0 => "Boot",
        1 => "System",
        2 => "Automatic",
        3 => "Manual",
        4 => "Disabled",
        _ => "",
    };
}
