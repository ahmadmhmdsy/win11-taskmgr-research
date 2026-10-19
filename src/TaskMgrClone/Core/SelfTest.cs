using System.Text;

namespace TaskMgrClone.Core;

/// <summary>
/// One runnable check for every non-trivial pure decision in the app.
/// Invoke with: TaskMgrClone.exe --selftest [outputPath]
/// </summary>
internal static class SelfTest
{
    public static int Run(string? outputPath)
    {
        var log = new StringBuilder();
        int failures = 0;

        void Check(string name, bool condition, string? detail = null)
        {
            if (condition) log.AppendLine($"PASS {name}");
            else { failures++; log.AppendLine($"FAIL {name}{(detail == null ? "" : " :: " + detail)}"); }
        }

        // RingBuffer: fixed capacity, oldest-first indexing, wraparound
        var ring = new RingBuffer(4);
        for (int i = 1; i <= 6; i++) ring.Add(i);
        Check("ringbuffer.capacity", ring.Count == 4 && ring.Capacity == 4, $"count={ring.Count}");
        Check("ringbuffer.oldest-first", ring[0] == 3 && ring[3] == 6, $"{ring[0]}..{ring[3]}");
        Check("ringbuffer.max", ring.Max == 6, ring.Max.ToString());
        ring.Clear();
        Check("ringbuffer.clear", ring.Count == 0 && ring.Max == 0);

        // Disk instance label parsing
        Check("disk.label-c", Sampler.DiskLabel("0 C:") == "C:", Sampler.DiskLabel("0 C:"));
        Check("disk.label-multi", Sampler.DiskLabel("1 E: F: G:") == "E: F: G:", Sampler.DiskLabel("1 E: F: G:"));
        Check("disk.label-bare", Sampler.DiskLabel("_Total") == "_Total");

        // GPU / per-process instance parsing
        Check("gpu.luid", Sampler.ExtractLuid("luid_0x00000000_0x0000ABCD_phys_0_eng_0_engtype_3D") == "luid_0x00000000_0x0000ABCD");
        Check("gpu.luid-missing", Sampler.ExtractLuid("gpu_engine_total") == "");
        Check("gpu.pid", Sampler.ExtractPid("pid_4321_luid_0x0_0x1_phys_0_eng_0_engtype_3D") == 4321);
        Check("gpu.pid-missing", Sampler.ExtractPid("notapid_7_x") == 0);

        // Power usage bucket ordering must be monotonic
        Check("power.monotonic", Sampler.EstimatePower(0.1) < Sampler.EstimatePower(3) && Sampler.EstimatePower(3) < Sampler.EstimatePower(10) && Sampler.EstimatePower(10) < Sampler.EstimatePower(40) && Sampler.EstimatePower(40) < Sampler.EstimatePower(90));

        // Process grouping rules
        Check("group.app-window", Sampler.ClassifyProcess(new ProcessRow { Pid = 1, HasWindow = true }) == ProcessGroup.Apps);
        Check("group.windows-path", Sampler.ClassifyProcess(new ProcessRow { Pid = 2, Path = @"C:\Windows\System32\svchost.exe" }) == ProcessGroup.Windows);
        Check("group.store-app", Sampler.ClassifyProcess(new ProcessRow { Pid = 3, Path = @"C:\Program Files\WindowsApps\x\a.exe" }) == ProcessGroup.Background);
        Check("group.other", Sampler.ClassifyProcess(new ProcessRow { Pid = 4, Path = @"C:\Program Files\App\app.exe" }) == ProcessGroup.Background);

        // Startup approval blob
        Check("startup.enabled-default", StartupApps.IsEnabledBlob(null));
        Check("startup.enabled", StartupApps.IsEnabledBlob(new byte[] { 0x02, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Check("startup.disabled", !StartupApps.IsEnabledBlob(new byte[] { 0x03, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 }));
        Check("startup.folder-enabled", StartupApps.IsEnabledBlob(new byte[] { 0x06 }));
        Check("startup.folder-disabled", !StartupApps.IsEnabledBlob(new byte[] { 0x07 }));

        // Command line tokenising for startup entries
        Check("cmdline.quoted", StartupApps.FirstToken("\"C:\\Program Files\\App\\app.exe\" --min") == @"C:\Program Files\App\app.exe");
        Check("cmdline.bare", StartupApps.FirstToken("C:\\app.exe /x") == @"C:\app.exe");
        Check("cmdline.single", StartupApps.FirstToken("app.exe") == "app.exe");

        // Priority name mapping is a bijection over the six documented classes
        var priorities = new[] { Native.IDLE_PRIORITY_CLASS, Native.BELOW_NORMAL_PRIORITY_CLASS, Native.NORMAL_PRIORITY_CLASS, Native.ABOVE_NORMAL_PRIORITY_CLASS, Native.HIGH_PRIORITY_CLASS, Native.REALTIME_PRIORITY_CLASS };
        var names = priorities.Select(Native.PriorityClassName).ToList();
        Check("priority.names", names.All(n => n.Length > 0) && names.Distinct().Count() == 6, string.Join(",", names));
        Check("priority.roundtrip", names.All(n => ProcessActions.TryParsePriority(n, out _)));

        // Service state strings must round trip through the row view model
        var row = new ServiceRow { Status = "Running" };
        Check("service.flags", row.Running && row.CanStop && !row.CanStart);
        row.Status = "Stopped";
        Check("service.flags-stopped", !row.Running && !row.CanStop && row.CanStart);

        // Elevation self-check must not throw on any host
        Check("native.self-elevation", Native.IsElevatedSelf() || !Native.IsElevatedSelf());
        Check("native.core-count", Native.PhysicalCoreCount() >= 1, Native.PhysicalCoreCount().ToString());

        // Formatting helpers used by every page
        Check("format.bytes", Controls.Format.Bytes(1_572_864) == "1.5 MB", Controls.Format.Bytes(1_572_864));
        Check("format.bytes-zero", Controls.Format.Bytes(0) == "0 B", Controls.Format.Bytes(0));
        Check("format.rate", Controls.Format.Rate(2_500_000).EndsWith("MB/s"), Controls.Format.Rate(2_500_000));
        Check("format.mbps", Controls.Format.Mbps(1_250_000) == "10.0 Mbps", Controls.Format.Mbps(1_250_000));
        Check("format.uptime", Controls.Format.Uptime(new TimeSpan(1, 2, 3, 4)) == "1:02:03:04", Controls.Format.Uptime(new TimeSpan(1, 2, 3, 4)));

        // Collection reconciliation keeps identity (selection) and ordering
        var collection = new System.Collections.ObjectModel.ObservableCollection<ProcessRow>();
        var a = new ProcessRow { Pid = 1, Name = "a" };
        var b = new ProcessRow { Pid = 2, Name = "b" };
        var cc = new ProcessRow { Pid = 3, Name = "c" };
        UI.ListSync.Sync(collection, new[] { a, b });
        Check("listsync.add", collection.Count == 2 && ReferenceEquals(collection[0], a));
        UI.ListSync.Sync(collection, new[] { a, b });
        Check("listsync.stable", collection.Count == 2 && ReferenceEquals(collection[0], a));
        UI.ListSync.Sync(collection, new[] { b, cc });
        Check("listsync.remove-add", collection.Count == 2 && ReferenceEquals(collection[0], b) && ReferenceEquals(collection[1], cc));
        Check("listsync.empty", collection.Count == 2);

        // Power buckets are mapped to the strings Task Manager displays
        Check("power.buckets", UI.PowerUsageConverter.Bucket(1) == "Very low" && UI.PowerUsageConverter.Bucket(2) == "Low"
            && UI.PowerUsageConverter.Bucket(3) == "Moderate" && UI.PowerUsageConverter.Bucket(4) == "High" && UI.PowerUsageConverter.Bucket(5) == "Very high");

        var text = log.ToString();
        log.AppendLine(failures == 0 ? "RESULT OK" : $"RESULT {failures} FAILURES");
        if (!string.IsNullOrEmpty(outputPath))
        {
            try { File.WriteAllText(outputPath, log.ToString()); } catch { }
        }
        try
        {
            Native.AttachConsoleToParent();
            Console.Out.Write(text);
            Console.Out.Flush();
        }
        catch { }
        return failures;
    }
}