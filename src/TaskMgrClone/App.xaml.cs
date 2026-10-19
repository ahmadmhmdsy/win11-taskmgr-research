using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskMgrClone.Core;
using TaskMgrClone.UI;

namespace TaskMgrClone;

public partial class App : Application
{
    public static AppSettings Settings { get; private set; } = new();

    /// <summary>App-history accumulator shared by the shell and the App history page.</summary>
    public static AppHistoryStore History { get; } = new();

    private void RenderPages(string[] args)
    {
        int index = Array.FindIndex(args, x => x.Equals("--renderpages", StringComparison.OrdinalIgnoreCase));
        var dir = index >= 0 && index + 1 < args.Length ? args[index + 1]
            : Path.Combine(Path.GetTempPath(), "TaskMgrClone-render");
        Directory.CreateDirectory(dir);
        var window = new MainWindow { Width = 1180, Height = 760, WindowStartupLocation = WindowStartupLocation.Manual, Left = 30, Top = 30, ShowInTaskbar = false };
        window.Show();
        window.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

        var names = new[] { "Processes", "Performance", "AppHistory", "Startup", "Users", "Details", "Services" };
        var frame = new DispatcherFrame();
        int step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        timer.Tick += (_, _) =>
        {
            if (step > 0)
            {
                int shot = step - 1;
                try
                {
                    var scale = VisualTreeHelper.GetDpi(window).DpiScaleX;
                    int w = Math.Max(1, (int)Math.Round(window.ActualWidth * scale));
                    int h = Math.Max(1, (int)Math.Round(window.ActualHeight * scale));
                    var bmp = new RenderTargetBitmap(w, h, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
                    bmp.Render(window);
                    var encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(bmp));
                    using var fs = File.Create(Path.Combine(dir, $"{shot}-{names[shot]}.png"));
                    encoder.Save(fs);
                }
                catch (Exception ex) { File.AppendAllText(Path.Combine(dir, "errors.txt"), ex + Environment.NewLine); }
            }
            if (step >= 7) { timer.Stop(); frame.Continue = false; return; }
            window.NavigateToPage(step);
            step++;
        };
        timer.Start();
        Dispatcher.PushFrame(frame);
        timer.Stop();
        window.Close();
        Shutdown(0);
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        var args = e.Args;
        if (args.Any(a => a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)))
        {
            var output = args.SkipWhile(a => !a.Equals("--selftest", StringComparison.OrdinalIgnoreCase)).Skip(1).FirstOrDefault();
            Environment.ExitCode = SelfTest.Run(output);
            Shutdown(Environment.ExitCode);
            return;
        }

        int killIndex = Array.FindIndex(args, x => x.Equals("--kill", StringComparison.OrdinalIgnoreCase));
        if (killIndex >= 0 && killIndex + 1 < args.Length && int.TryParse(args[killIndex + 1], out var killPid))
        {
            Native.AttachConsoleToParent();
            bool killed = ProcessActions.EndTaskGraceful(killPid);
            Console.WriteLine($"kill {killPid} -> {(killed ? "ok" : "failed")}");
            Environment.ExitCode = killed ? 0 : 1;
            Shutdown(Environment.ExitCode);
            return;
        }

        if (args.Any(a => a.Equals("--sampledump", StringComparison.OrdinalIgnoreCase)))
        {
            Native.AttachConsoleToParent();
            var sb = new System.Text.StringBuilder();
            using var sampler = new Sampler();
            System.Threading.Thread.Sleep(1200);
            for (int i = 0; i < 3; i++)
            {
                sampler.SampleOnce();
                System.Threading.Thread.Sleep(1000);
            }
            var sys = sampler.System;
            sb.AppendLine($"cpu={sys.CpuPercent:0.0}% speed={sys.CpuSpeedGhz:0.00}GHz base={sys.CpuBaseGhz:0.00} cores={sys.CpuCores}/{sys.CpuLogical} procs={sys.CpuProcesses} threads={sys.CpuThreads} handles={sys.CpuHandles} uptime={sys.Uptime}");
            sb.AppendLine($"mem total={sys.MemTotal / 1073741824.0:0.0}GB avail={sys.MemAvailable / 1073741824.0:0.0}GB inuse={sys.MemInUse / 1073741824.0:0.0}GB cached={sys.MemCached / 1073741824.0:0.0}GB commit={sys.MemCommit / 1073741824.0:0.0}GB pagedPool={sys.MemPagedPool / 1048576.0:0}MB npPool={sys.MemNonPagedPool / 1048576.0:0}MB");
            sb.AppendLine($"disks={sys.Disks.Count} nets={sys.Networks.Count} gpus={sys.Gpus.Count} perCore={sys.PerCorePercent.Length} rows={sampler.Processes.Count}");
            foreach (var d in sys.Disks) sb.AppendLine($"  disk {d.Instance} label={d.Label} active={d.ActivePercent:0}% read={d.ReadBytesPerSec:0}B/s write={d.WriteBytesPerSec:0}B/s cap={d.Capacity / 1073741824.0:0.0}GB free={d.FreeSpace / 1073741824.0:0.0}GB");
            foreach (var n in sys.Networks) sb.AppendLine($"  net {n.Name} kind={n.Kind} total={n.TotalBytesPerSec:0}B/s link={n.LinkSpeedBps / 1000000.0:0}Mbps ipv4={n.Ipv4} ipv6={n.Ipv6}");
            foreach (var g in sys.Gpus) sb.AppendLine($"  gpu {g.Name} 3d={g.Percent3D:0}% copy={g.PercentCopy:0}% ve={g.PercentVideoEncode:0}% vd={g.PercentVideoDecode:0}% ded={g.DedicatedUsed / 1073741824.0:0.0}GB luid={g.Luid}");
            foreach (var p in sampler.Processes.Values.OrderByDescending(p => p.WorkingSetBytes).Take(8))
                sb.AppendLine($"  proc {p.Pid} {p.Name} cpu={p.CpuPercent:0.0}% ws={p.WorkingSetBytes / 1048576.0:0.0}MB threads={p.ThreadCount} handles={p.HandleCount} status={p.Status} group={p.Group} eff={p.EfficiencyMode}");
            var path = Path.Combine(Path.GetTempPath(), "TaskMgrClone-sampledump.txt");
            File.WriteAllText(path, sb.ToString());
            Console.WriteLine("dump: " + path);
            Environment.ExitCode = 0;
            Shutdown(0);
            return;
        }

        if (args.Any(a => a.Equals("--renderpages", StringComparison.OrdinalIgnoreCase)))
        {
            Settings = AppSettings.Load();
            ThemeManager.Apply(Settings.Theme);
            RenderPages(args);
            return;
        }

        Settings = AppSettings.Load();
        ThemeManager.Apply(Settings.Theme);
        base.OnStartup(e);
        var window = new MainWindow();
        MainWindow = window;
        window.Show();
    }
}