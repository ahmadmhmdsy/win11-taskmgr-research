using System.Runtime.InteropServices;

namespace TaskMgrClone.Core;

internal static class Sessions
{
    public static List<UserRow> Enumerate(Dictionary<int, ProcessRow> processes)
    {
        var rows = new List<UserRow>();
        IntPtr buffer = IntPtr.Zero;
        uint count = 0;
        if (!Native.WTSEnumerateSessions(IntPtr.Zero, 0, 1, ref buffer, ref count)) return rows;
        try
        {
            var size = Marshal.SizeOf<Native.WTS_SESSION_INFO>();
            for (int i = 0; i < count; i++)
            {
                var info = Marshal.PtrToStructure<Native.WTS_SESSION_INFO>(buffer + i * size);
                var station = Marshal.PtrToStringUni(info.pWinStationName) ?? "";
                if (info.SessionID == 0 && string.IsNullOrEmpty(station)) continue;
                var user = QueryString(info.SessionID, Native.WTSUserName);
                if (string.IsNullOrEmpty(user)) continue;
                var domain = QueryString(info.SessionID, Native.WTSDomainName);
                var client = QueryString(info.SessionID, Native.WTSClientName);
                rows.Add(new UserRow
                {
                    SessionId = info.SessionID,
                    UserName = string.IsNullOrEmpty(domain) ? user : domain + "\\" + user,
                    Status = DescribeState(info.State),
                    ClientName = client,
                    SessionName = station,
                });
            }
        }
        finally { Native.WTSFreeMemory(buffer); }

        foreach (var row in rows)
        {
            var owned = processes.Values.Where(p => p.SessionId == row.SessionId).ToList();
            row.ProcessCount = owned.Count;
            row.CpuPercent = owned.Sum(p => p.CpuPercent);
            row.MemoryBytes = (ulong)owned.Sum(p => (long)p.WorkingSetBytes);
            row.DiskBytesPerSec = owned.Sum(p => p.DiskBytesPerSec);
        }
        return rows;
    }

    private static string QueryString(uint sessionId, int infoClass)
    {
        if (!Native.WTSQuerySessionInformation(IntPtr.Zero, sessionId, infoClass, out var buffer, out var bytes)) return "";
        try { return bytes > 2 ? Marshal.PtrToStringUni(buffer) ?? "" : ""; }
        finally { Native.WTSFreeMemory(buffer); }
    }

    private static string DescribeState(int state) => state switch
    {
        0 => "Active",
        1 => "Connected",
        2 => "ConnectQuery",
        3 => "Shadow",
        4 => "Disconnected",
        5 => "Idle",
        6 => "Listen",
        7 => "Reset",
        8 => "Down",
        9 => "Init",
        _ => "Unknown",
    };
}
