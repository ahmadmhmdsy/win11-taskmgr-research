using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using Microsoft.Win32;
using TaskMgrClone.Controls;
using TaskMgrClone.Core;
using TaskMgrClone.UI;

namespace TaskMgrClone.Pages;

/// <summary>
/// Windows 11 Task Manager "Performance" tab: resource tiles on the left, big graph and
/// live statistics for the selected resource on the right.
/// </summary>
public partial class PerformancePage : PageBase
{
    private enum TileKind { Cpu, Memory, Disk, Network, Gpu }

    /// <summary>One row in the left resource list, with its own graph history.</summary>
    private sealed class Tile
    {
        public required string Key { get; init; }
        public required TileKind Kind { get; init; }
        public string Instance = "";
        public int Index;
        public string DisplayTitle = "";
        public Border Root = null!;
        public Border Pill = null!;
        public MiniGraph Graph = null!;
        public TextBlock Title = null!;
        public TextBlock Line1 = null!;
        public TextBlock Line2 = null!;
        public readonly RingBuffer History = new(60);
        public RingBuffer? Send;
        public RingBuffer? Receive;
        public RingBuffer[]? Engines;
    }

    private readonly List<Tile> _tiles = new();
    private readonly Dictionary<string, TextBlock> _stats = new();
    private readonly Dictionary<string, string> _ipv4 = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _ipv6 = new(StringComparer.OrdinalIgnoreCase);
    private readonly SolidColorBrush _compressedBrush = new(Color.FromRgb(0x2C, 0x6D, 0x9C));
    // Real Task Manager draws GPU graphs in violet (reference tm-processes.png: #DE68F2); the
    // palette's GpuLine is a warm accent, so the GPU page uses the reference colour instead.
    private readonly SolidColorBrush _gpuBrush = new(Color.FromRgb(0xDE, 0x68, 0xF2));
    private readonly Dictionary<string, (ulong Capacity, ulong Free)> _diskSpace = new(StringComparer.OrdinalIgnoreCase);

    private SystemSnapshot? _last;
    private Dictionary<int, ProcessRow>? _lastProcesses;
    private string _selectedKey = "cpu";
    private bool _cpuLogicalView;
    private bool _networkAddressesLoaded;
    private string _cpuModel = "";
    private string _memSpeed = "";
    private string _memFormFactor = "";
    private string _memSlotsText = "";
    private int _samples;
    private TextBlock? _legendInUse;
    private TextBlock? _legendCached;
    private TextBlock? _legendFree;
    private TextBlock? _legendCompressed;

    public override string Title => "Performance";
    public override bool CanSearch => false;

    public PerformancePage()
    {
        InitializeComponent();
        _compressedBrush.Freeze();
        _gpuBrush.Freeze();

        BuildStatCells();
        BuildMemoryLegend();
        _cpuModel = ReadCpuModelName();
        LoadFirmwareMemoryInfo();

        // CPU + Memory are always present; disks/adapters/GPUs appear on the first sample.
        EnsureTiles(null);
        ApplySelectionVisuals();
        UpdateDetail(null);
    }

    // ------------------------------------------------------------------ contract

    public override void OnSample(SystemSnapshot system, Dictionary<int, ProcessRow> processes)
    {
        if (system == null) return;
        try
        {
            _last = system;
            if (processes != null) _lastProcesses = processes;

            _samples++;
            EnsureTiles(system);
            RefreshDiskSpace(system);
            AppendHistories(system);
            UpdateTileTexts(system);
            UpdateDetail(system);
            InvalidateGraphs();
        }
        catch
        {
            // A performance page must never take the app down.
        }
    }

    // ------------------------------------------------------------------ resource list

    private void EnsureTiles(SystemSnapshot? system)
    {
        var descriptors = new List<(string Key, TileKind Kind, string Instance, int Index, string Title)>
        {
            ("cpu", TileKind.Cpu, "", -1, "CPU"),
            ("memory", TileKind.Memory, "", -1, "Memory"),
        };
        if (system != null)
        {
            // Stable order: the sampler may re-sort adapter/disk lists by activity, but the
            // tile list (and therefore selection + history) must not reshuffle every tick.
            foreach (var disk in system.Disks.OrderBy(d => d.Instance, StringComparer.OrdinalIgnoreCase))
                descriptors.Add(("disk:" + (disk.Instance ?? ""), TileKind.Disk, disk.Instance ?? "", -1, DiskTitle(disk)));

            foreach (var adapter in system.Networks.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase))
                descriptors.Add(("net:" + (adapter.Name ?? ""), TileKind.Network, adapter.Name ?? "", -1,
                    string.IsNullOrWhiteSpace(adapter.Name) ? "Network" : CleanAdapterName(adapter.Name)));

            for (int i = 0; i < system.Gpus.Count; i++)
                descriptors.Add(("gpu:" + i, TileKind.Gpu, "", i, "GPU " + i));
        }

        bool same = descriptors.Count == _tiles.Count;
        if (same)
        {
            for (int i = 0; i < descriptors.Count; i++)
            {
                if (string.Equals(descriptors[i].Key, _tiles[i].Key, StringComparison.Ordinal)) continue;
                same = false;
                break;
            }
        }
        if (same) return;

        RebuildTiles(descriptors);
    }

    private void RebuildTiles(List<(string Key, TileKind Kind, string Instance, int Index, string Title)> descriptors)
    {
        TileHost.Children.Clear();
        _tiles.Clear();

        foreach (var descriptor in descriptors)
        {
            var tile = CreateTile(descriptor.Key, descriptor.Kind, descriptor.Instance, descriptor.Index, descriptor.Title);
            _tiles.Add(tile);
            TileHost.Children.Add(tile.Root);
        }

        if (!_tiles.Any(t => string.Equals(t.Key, _selectedKey, StringComparison.Ordinal)))
            _selectedKey = _tiles.Count > 0 ? _tiles[0].Key : "cpu";

        ApplySelectionVisuals();
    }

    private Tile CreateTile(string key, TileKind kind, string instance, int index, string title)
    {
        var tile = new Tile
        {
            Key = key,
            Kind = kind,
            Instance = instance,
            Index = index,
            DisplayTitle = title,
        };
        if (kind == TileKind.Network)
        {
            tile.Send = new RingBuffer(60);
            tile.Receive = new RingBuffer(60);
        }
        else if (kind == TileKind.Gpu)
        {
            tile.Engines = new[] { new RingBuffer(60), new RingBuffer(60), new RingBuffer(60), new RingBuffer(60) };
        }

        var root = new Border
        {
            Height = 76,
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            SnapsToDevicePixels = true,
            ToolTip = title,
        };
        tile.Root = root;

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(3) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(94) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var pill = new Border
        {
            Width = 3,
            Height = 42,
            CornerRadius = new CornerRadius(2),
            Background = Brushes.Transparent,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center,
        };
        tile.Pill = pill;
        Grid.SetColumn(pill, 0);
        grid.Children.Add(pill);

        var graphHost = new Border
        {
            Width = 76,
            Height = 46,
            Margin = new Thickness(10, 0, 8, 0),
            VerticalAlignment = VerticalAlignment.Center,
            BorderBrush = Res("BorderBrush"),
            BorderThickness = new Thickness(1),
            Background = Res("ListBg"),
        };
        var miniGraph = new MiniGraph
        {
            Data = tile.History,
            Line = BrushFor(kind),
            Max = 100,
            AutoScale = kind == TileKind.Network,
        };
        tile.Graph = miniGraph;
        graphHost.Child = miniGraph;
        Grid.SetColumn(graphHost, 1);
        grid.Children.Add(graphHost);

        var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(4, 0, 10, 0) };
        var titleText = new TextBlock
        {
            Text = title,
            FontSize = 15,
            Foreground = Res("TextPrimary"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        var line1 = new TextBlock
        {
            FontSize = 12,
            Foreground = Res("TextSecondary"),
            Margin = new Thickness(0, 3, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        var line2 = new TextBlock
        {
            FontSize = 12,
            Foreground = Res("TextSecondary"),
            Margin = new Thickness(0, 1, 0, 0),
            Visibility = Visibility.Collapsed,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        tile.Title = titleText;
        tile.Line1 = line1;
        tile.Line2 = line2;
        text.Children.Add(titleText);
        text.Children.Add(line1);
        text.Children.Add(line2);
        Grid.SetColumn(text, 2);
        grid.Children.Add(text);

        root.Child = grid;
        root.MouseLeftButtonDown += (_, e) =>
        {
            SelectTile(tile.Key);
            e.Handled = true;
        };
        root.MouseEnter += (_, _) =>
        {
            if (!string.Equals(tile.Key, _selectedKey, StringComparison.Ordinal))
                root.Background = Res("HoverBg");
        };
        root.MouseLeave += (_, _) =>
        {
            if (!string.Equals(tile.Key, _selectedKey, StringComparison.Ordinal))
                root.Background = Brushes.Transparent;
        };

        return tile;
    }

    private void SelectTile(string key)
    {
        if (string.Equals(_selectedKey, key, StringComparison.Ordinal)) return;
        _selectedKey = key;
        ApplySelectionVisuals();
        UpdateDetail(_last);
        InvalidateGraphs();
    }

    private void ApplySelectionVisuals()
    {
        foreach (var tile in _tiles)
        {
            bool selected = string.Equals(tile.Key, _selectedKey, StringComparison.Ordinal);
            tile.Root.Background = selected ? Res("SelectionBg") : Brushes.Transparent;
            tile.Pill.Background = selected ? Res("AccentBrush") : Brushes.Transparent;
        }
    }

    private void AppendHistories(SystemSnapshot system)
    {
        foreach (var tile in _tiles)
        {
            switch (tile.Kind)
            {
                case TileKind.Cpu:
                    tile.History.Add(system.CpuPercent);
                    break;
                case TileKind.Memory:
                    tile.History.Add(system.MemPercentOfTotal);
                    break;
                case TileKind.Disk:
                    tile.History.Add(FindDisk(system, tile.Instance)?.ActivePercent ?? 0);
                    break;
                case TileKind.Network:
                {
                    var adapter = FindNetwork(system, tile.Instance);
                    tile.History.Add(adapter?.TotalBytesPerSec ?? 0);
                    tile.Send?.Add(adapter?.SendBytesPerSec ?? 0);
                    tile.Receive?.Add(adapter?.ReceiveBytesPerSec ?? 0);
                    break;
                }
                case TileKind.Gpu:
                {
                    var gpu = GpuAt(system, tile.Index);
                    tile.History.Add(gpu == null ? 0 : MaxGpuEngine(gpu));
                    if (tile.Engines != null)
                    {
                        tile.Engines[0].Add(gpu?.Percent3D ?? 0);
                        tile.Engines[1].Add(gpu?.PercentCopy ?? 0);
                        tile.Engines[2].Add(gpu?.PercentVideoEncode ?? 0);
                        tile.Engines[3].Add(gpu?.PercentVideoDecode ?? 0);
                    }
                    break;
                }
            }
        }
    }

    private void UpdateTileTexts(SystemSnapshot? system)
    {
        if (system == null) return;

        foreach (var tile in _tiles)
        {
            string line2;
            switch (tile.Kind)
            {
                case TileKind.Cpu:
                    tile.Line1.Text = $"{system.CpuPercent:0}%  {system.CpuSpeedGhz:0.00} GHz";
                    line2 = "";
                    break;
                case TileKind.Memory:
                    tile.Line1.Text = $"{Gb(system.MemInUse):0.0}/{Gb(system.MemTotal):0.0} GB ({system.MemPercentOfTotal:0}%)";
                    line2 = "";
                    break;
                case TileKind.Disk:
                {
                    var disk = FindDisk(system, tile.Instance);
                    var space = disk == null ? (Capacity: 0UL, Free: 0UL) : DiskSpace(disk);
                    tile.Line1.Text = disk != null && !string.IsNullOrEmpty(disk.MediaType)
                        ? disk.MediaType
                        : space.Capacity > 0 ? Format.Bytes(space.Capacity) : "";
                    line2 = disk != null ? Format.Percent(disk.ActivePercent) : "0%";
                    break;
                }
                case TileKind.Network:
                {
                    var adapter = FindNetwork(system, tile.Instance);
                    tile.Line1.Text = adapter?.Kind ?? "";
                    line2 = adapter == null
                        ? ""
                        : $"S: {adapter.SendBytesPerSec * 8 / 1_000_000:0.0} R: {adapter.ReceiveBytesPerSec * 8 / 1_000_000:0.0} Mbps";
                    break;
                }
                case TileKind.Gpu:
                {
                    var gpu = GpuAt(system, tile.Index);
                    tile.Line1.Text = gpu?.Name ?? "";
                    var percent = gpu == null ? 0 : MaxGpuEngine(gpu);
                    if (gpu == null)
                        line2 = "";
                    else if (gpu.Temperature is > 0)
                        line2 = $"{percent:0}% ({gpu.Temperature:0} \u00B0C)";
                    else
                        line2 = $"{percent:0}% (N/A)";
                    break;
                }
                default:
                    line2 = "";
                    break;
            }

            tile.Line1.Visibility = string.IsNullOrEmpty(tile.Line1.Text) ? Visibility.Collapsed : Visibility.Visible;
            tile.Line2.Text = line2;
            tile.Line2.Visibility = string.IsNullOrEmpty(line2) ? Visibility.Collapsed : Visibility.Visible;
            tile.Graph.Line = BrushFor(tile.Kind);
        }
    }

    // ------------------------------------------------------------------ detail pane

    private void UpdateDetail(SystemSnapshot? system)
    {
        var selected = _tiles.FirstOrDefault(t => string.Equals(t.Key, _selectedKey, StringComparison.Ordinal))
                       ?? _tiles.FirstOrDefault();

        CpuPanel.Visibility = selected?.Kind == TileKind.Cpu ? Visibility.Visible : Visibility.Collapsed;
        MemoryPanel.Visibility = selected?.Kind == TileKind.Memory ? Visibility.Visible : Visibility.Collapsed;
        DiskPanel.Visibility = selected?.Kind == TileKind.Disk ? Visibility.Visible : Visibility.Collapsed;
        NetworkPanel.Visibility = selected?.Kind == TileKind.Network ? Visibility.Visible : Visibility.Collapsed;
        GpuPanel.Visibility = selected?.Kind == TileKind.Gpu ? Visibility.Visible : Visibility.Collapsed;

        if (system == null || selected == null) return;

        switch (selected.Kind)
        {
            case TileKind.Cpu: UpdateCpuDetail(system, selected); break;
            case TileKind.Memory: UpdateMemoryDetail(system, selected); break;
            case TileKind.Disk: UpdateDiskDetail(system, selected); break;
            case TileKind.Network: UpdateNetworkDetail(system, selected); break;
            case TileKind.Gpu: UpdateGpuDetail(system, selected); break;
        }
    }

    private void UpdateCpuDetail(SystemSnapshot system, Tile tile)
    {
        CpuModelText.Text = _cpuModel;
        CpuModelText.Visibility = string.IsNullOrWhiteSpace(_cpuModel) ? Visibility.Collapsed : Visibility.Visible;

        CpuArea.Visibility = _cpuLogicalView ? Visibility.Collapsed : Visibility.Visible;
        CpuHeat.Visibility = _cpuLogicalView ? Visibility.Visible : Visibility.Collapsed;
        CpuGraphOverallMenu.IsChecked = !_cpuLogicalView;
        CpuGraphLogicalMenu.IsChecked = _cpuLogicalView;

        SetAreaSeries(CpuArea, false, (tile.History, Res("CpuLine")));
        CpuHeat.Values = system.PerCorePercent;
        UpdateHeatmapLayout(system.PerCorePercent.Length);

        SetStat("cpu.util", Format.Percent(system.CpuPercent));
        SetStat("cpu.speed", system.CpuSpeedGhz > 0.01 ? $"{system.CpuSpeedGhz:0.00} GHz" : "");
        SetStat("cpu.processes", system.CpuProcesses > 0 ? system.CpuProcesses.ToString("N0") : "");
        SetStat("cpu.threads", system.CpuThreads > 0 ? system.CpuThreads.ToString("N0") : "");
        SetStat("cpu.handles", system.CpuHandles > 0 ? system.CpuHandles.ToString("N0") : "");
        SetStat("cpu.uptime", system.Uptime > TimeSpan.Zero ? Format.Uptime(system.Uptime) : "");
    }

    private void UpdateHeatmapLayout(int logicalProcessors)
    {
        if (logicalProcessors <= 0)
        {
            CpuHeat.Columns = 4;
            return;
        }
        double width = CpuGraphHost.ActualWidth > 20 ? CpuGraphHost.ActualWidth : 520;
        double height = CpuGraphHost.ActualHeight > 20 ? CpuGraphHost.ActualHeight : 140;
        int columns = (int)Math.Round(Math.Sqrt(logicalProcessors * width / height));
        CpuHeat.Columns = Math.Clamp(columns, 1, Math.Max(1, logicalProcessors));
    }

    private void UpdateMemoryDetail(SystemSnapshot system, Tile tile)
    {
        MemTotalText.Text = system.MemTotal > 0 ? Format.Bytes(system.MemTotal) : "";
        MemTotalText.Visibility = string.IsNullOrEmpty(MemTotalText.Text) ? Visibility.Collapsed : Visibility.Visible;

        SetAreaSeries(MemoryArea, false, (tile.History, Res("MemLine")));

        var inUse = system.MemInUse;
        var cached = system.MemStandby > 0 ? system.MemStandby : system.MemCached;
        var free = system.MemFree;
        if (free == 0 && system.MemTotal > inUse + cached) free = system.MemTotal - inUse - cached;
        var compressed = Math.Min(system.MemCompressed, inUse);

        UpdateMemoryComposition(inUse, cached, free, compressed);

        if (_legendInUse != null) _legendInUse.Text = Format.Bytes(inUse);
        if (_legendCached != null) _legendCached.Text = Format.Bytes(cached);
        if (_legendFree != null) _legendFree.Text = Format.Bytes(free);
        if (_legendCompressed != null) _legendCompressed.Text = Format.Bytes(compressed);

        SetStat("mem.inuse", $"{Gb(inUse):0.0}/{Gb(system.MemTotal):0.0} GB ({system.MemPercentOfTotal:0}%)");
        SetStat("mem.available", system.MemAvailable > 0 ? Format.Bytes(system.MemAvailable) : "");
        SetStat("mem.committed", system.MemCommitLimit > 0 ? $"{Gb(system.MemCommit):0.0}/{Gb(system.MemCommitLimit):0.0} GB" : "");
        SetStat("mem.cached", cached > 0 ? Format.Bytes(cached) : "");
        SetStat("mem.pagedpool", system.MemPagedPool > 0 ? Format.Bytes(system.MemPagedPool) : "");
        SetStat("mem.nonpaged", system.MemNonPagedPool > 0 ? Format.Bytes(system.MemNonPagedPool) : "");
        SetStat("mem.speed", system.MemSpeed.Length > 0 ? system.MemSpeed : _memSpeed);
        SetStat("mem.formfactor", system.MemFormFactor.Length > 0 ? system.MemFormFactor : _memFormFactor);
        SetStat("mem.slots", system.MemTotalSlots > 0
            ? $"{system.MemUsedSlots} of {system.MemTotalSlots}"
            : _memSlotsText);
    }

    private void UpdateMemoryComposition(double inUse, double cached, double free, double compressed)
    {
        double total = Math.Max(1, inUse + cached + free + compressed);
        MemoryComposition.Total = total;
        MemoryComposition.Segments.Clear();
        AddSegment("In use", inUse, Res("AccentBrush"));
        AddSegment("Cached", cached, Res("MemLine"));
        AddSegment("Free", free, Res("BarEmpty"));
        AddSegment("Compressed", compressed, _compressedBrush);
        MemoryComposition.InvalidateVisual();
    }

    private void AddSegment(string label, double value, Brush brush)
    {
        if (value <= 0) return;
        MemoryComposition.Segments.Add(new CompositionSegment { Label = label, Value = value, Brush = brush });
    }

    private void UpdateDiskDetail(SystemSnapshot system, Tile tile)
    {
        var disk = FindDisk(system, tile.Instance);
        DiskTitleText.Text = disk != null ? DiskTitle(disk) : tile.DisplayTitle;
        SetAreaSeries(DiskArea, false, (tile.History, Res("DiskLine")));

        if (disk == null)
        {
            SetStat("disk.active", "");
            SetStat("disk.response", "");
            SetStat("disk.read", "");
            SetStat("disk.write", "");
            SetStat("disk.type", "");
            SetStat("disk.capacity", "");
            SetStat("disk.free", "");
            return;
        }

        var space = DiskSpace(disk);
        SetStat("disk.active", Format.Percent(disk.ActivePercent));
        SetStat("disk.response", $"{Math.Max(0, disk.AvgResponseMs):0.00} ms");
        SetStat("disk.read", Format.Rate(disk.ReadBytesPerSec));
        SetStat("disk.write", Format.Rate(disk.WriteBytesPerSec));
        SetStat("disk.type", disk.MediaType);
        SetStat("disk.capacity", space.Capacity > 0 ? Format.Bytes(space.Capacity) : "");
        SetStat("disk.free", space.Free > 0 ? Format.Bytes(space.Free) : "");
    }

    private void UpdateNetworkDetail(SystemSnapshot system, Tile tile)
    {
        var adapter = FindNetwork(system, tile.Instance);
        NetTitleText.Text = adapter != null ? CleanAdapterName(adapter.Name) : tile.DisplayTitle;
        NetKindText.Text = adapter?.Kind ?? "";
        NetKindText.Visibility = string.IsNullOrEmpty(NetKindText.Text) ? Visibility.Collapsed : Visibility.Visible;

        EnsureNetworkAddresses();

        if (adapter != null && tile.Receive != null && tile.Send != null)
            SetAreaSeries(NetArea, true, (tile.Receive, Res("AccentBrush")), (tile.Send, Res("NetLine")));
        else
            SetAreaSeries(NetArea, true, (tile.History, Res("NetLine")));

        if (adapter == null)
        {
            SetStat("net.send", "");
            SetStat("net.receive", "");
            SetStat("net.link", "");
            SetStat("net.adapter", tile.DisplayTitle);
            SetStat("net.type", "");
            SetStat("net.ipv4", "");
            SetStat("net.ipv6", "");
            return;
        }

        SetStat("net.send", Format.Mbps(adapter.SendBytesPerSec));
        SetStat("net.receive", Format.Mbps(adapter.ReceiveBytesPerSec));
        SetStat("net.link", LinkSpeedText(adapter.LinkSpeedBps));
        SetStat("net.adapter", CleanAdapterName(adapter.Name));
        SetStat("net.type", adapter.Kind);
        SetStat("net.ipv4", LookupAddress(_ipv4, adapter.Name));
        SetStat("net.ipv6", LookupAddress(_ipv6, adapter.Name));
    }

    private void UpdateGpuDetail(SystemSnapshot system, Tile tile)
    {
        var gpu = GpuAt(system, tile.Index);
        GpuTitleText.Text = "GPU " + tile.Index;
        GpuAdapterText.Text = gpu?.Name ?? "";
        GpuAdapterText.Visibility = string.IsNullOrEmpty(GpuAdapterText.Text) ? Visibility.Collapsed : Visibility.Visible;

        var engines = tile.Engines;
        if (engines != null)
        {
            ConfigEngineGraph(Gpu3DGraph, engines[0]);
            ConfigEngineGraph(GpuCopyGraph, engines[1]);
            ConfigEngineGraph(GpuEncodeGraph, engines[2]);
            ConfigEngineGraph(GpuDecodeGraph, engines[3]);
        }

        Gpu3DPercent.Text = Format.Percent(gpu?.Percent3D ?? 0);
        GpuCopyPercent.Text = Format.Percent(gpu?.PercentCopy ?? 0);
        GpuEncodePercent.Text = Format.Percent(gpu?.PercentVideoEncode ?? 0);
        GpuDecodePercent.Text = Format.Percent(gpu?.PercentVideoDecode ?? 0);

        double dedicatedTotal = gpu?.DedicatedMemory ?? 0;
        double dedicatedUsed = DedicatedUsedBytes(gpu);
        double sharedTotal = gpu?.SharedMemory ?? 0;
        double sharedUsed = SharedUsedBytes();

        GpuDedicatedText.Text = dedicatedTotal > 0
            ? $"{Gb((ulong)dedicatedUsed):0.0}/{Gb((ulong)dedicatedTotal):0.0} GB"
            : "No dedicated memory";
        GpuDedicatedBar.Fill = _gpuBrush;
        GpuDedicatedBar.Value = dedicatedTotal > 0 ? Math.Clamp(dedicatedUsed / dedicatedTotal * 100, 0, 100) : 0;
        GpuDedicatedBar.InvalidateVisual();

        GpuSharedText.Text = sharedTotal > 0
            ? $"{Gb((ulong)sharedUsed):0.0}/{Gb((ulong)sharedTotal):0.0} GB"
            : "—";
        GpuSharedBar.Fill = _gpuBrush;
        GpuSharedBar.Value = sharedTotal > 0 ? Math.Clamp(sharedUsed / sharedTotal * 100, 0, 100) : 0;
        GpuSharedBar.InvalidateVisual();

        SetStat("gpu.util", gpu != null ? Format.Percent(MaxGpuEngine(gpu)) : "");
        SetStat("gpu.dedicated", dedicatedTotal > 0
            ? $"{Gb((ulong)dedicatedUsed):0.0}/{Gb((ulong)dedicatedTotal):0.0} GB"
            : "");
        SetStat("gpu.shared", sharedTotal > 0
            ? $"{Gb((ulong)sharedUsed):0.0}/{Gb((ulong)sharedTotal):0.0} GB"
            : "");
        SetStat("gpu.driver", gpu?.DriverVersion ?? "");
        SetStat("gpu.temp", gpu?.Temperature is > 0 ? $"{gpu.Temperature:0} \u00B0C" : "N/A");
    }

    private void ConfigEngineGraph(MiniGraph graph, RingBuffer data)
    {
        graph.Data = data;
        graph.Line = _gpuBrush;
        graph.Max = 100;
        graph.AutoScale = false;
        graph.ShowGrid = true;
    }

    private double DedicatedUsedBytes(GpuAdapterInfo? gpu)
    {
        if (gpu != null && gpu.DedicatedUsed > 0) return gpu.DedicatedUsed;
        if (_lastProcesses == null) return gpu?.DedicatedUsed ?? 0;
        double total = 0;
        foreach (var row in _lastProcesses.Values)
            if (row.HasGpu) total += row.GpuDedicatedBytes;
        return total;
    }

    private double SharedUsedBytes()
    {
        if (_lastProcesses == null) return 0;
        double total = 0;
        foreach (var row in _lastProcesses.Values)
            if (row.HasGpu) total += row.GpuSharedBytes;
        return total;
    }

    private void InvalidateGraphs()
    {
        foreach (var tile in _tiles) tile.Graph.InvalidateVisual();
        CpuArea.InvalidateVisual();
        CpuHeat.InvalidateVisual();
        MemoryArea.InvalidateVisual();
        MemoryComposition.InvalidateVisual();
        DiskArea.InvalidateVisual();
        NetArea.InvalidateVisual();
        Gpu3DGraph.InvalidateVisual();
        GpuCopyGraph.InvalidateVisual();
        GpuEncodeGraph.InvalidateVisual();
        GpuDecodeGraph.InvalidateVisual();
    }

    // ------------------------------------------------------------------ context menu

    private void OnCpuGraphOverall(object sender, RoutedEventArgs e)
    {
        _cpuLogicalView = false;
        CpuGraphOverallMenu.IsChecked = true;
        CpuGraphLogicalMenu.IsChecked = false;
        CpuArea.Visibility = Visibility.Visible;
        CpuHeat.Visibility = Visibility.Collapsed;
        UpdateDetail(_last);
        CpuArea.InvalidateVisual();
    }

    private void OnCpuGraphLogical(object sender, RoutedEventArgs e)
    {
        _cpuLogicalView = true;
        CpuGraphOverallMenu.IsChecked = false;
        CpuGraphLogicalMenu.IsChecked = true;
        CpuArea.Visibility = Visibility.Collapsed;
        CpuHeat.Visibility = Visibility.Visible;
        UpdateDetail(_last);
        CpuHeat.InvalidateVisual();
    }

    private void OnOpenResourceMonitor(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "resmon.exe", UseShellExecute = true });
        }
        catch
        {
            // resmon.exe may be unavailable; never let the link throw.
        }
    }

    // ------------------------------------------------------------------ statistics cells

    private void BuildStatCells()
    {
        AddStat(CpuStatsHost, "cpu.util", "Utilization");
        AddStat(CpuStatsHost, "cpu.speed", "Speed");
        AddStat(CpuStatsHost, "cpu.processes", "Processes");
        AddStat(CpuStatsHost, "cpu.threads", "Threads");
        AddStat(CpuStatsHost, "cpu.handles", "Handles");
        AddStat(CpuStatsHost, "cpu.uptime", "Up time");

        AddStat(MemoryStatsHost, "mem.inuse", "In use");
        AddStat(MemoryStatsHost, "mem.available", "Available");
        AddStat(MemoryStatsHost, "mem.committed", "Committed");
        AddStat(MemoryStatsHost, "mem.cached", "Cached");
        AddStat(MemoryStatsHost, "mem.pagedpool", "Paged pool");
        AddStat(MemoryStatsHost, "mem.nonpaged", "Non-paged pool");
        AddStat(MemoryStatsHost, "mem.speed", "Speed");
        AddStat(MemoryStatsHost, "mem.formfactor", "Form factor");
        AddStat(MemoryStatsHost, "mem.slots", "Slots used");

        AddStat(DiskStatsHost, "disk.active", "Active time");
        AddStat(DiskStatsHost, "disk.response", "Average response time");
        AddStat(DiskStatsHost, "disk.read", "Read speed");
        AddStat(DiskStatsHost, "disk.write", "Write speed");
        AddStat(DiskStatsHost, "disk.type", "Type");
        AddStat(DiskStatsHost, "disk.capacity", "Capacity");
        AddStat(DiskStatsHost, "disk.free", "Free");

        AddStat(NetStatsHost, "net.send", "Send");
        AddStat(NetStatsHost, "net.receive", "Receive");
        AddStat(NetStatsHost, "net.link", "Link speed");
        AddStat(NetStatsHost, "net.adapter", "Adapter name");
        AddStat(NetStatsHost, "net.type", "Connection type");
        AddStat(NetStatsHost, "net.ipv4", "IPv4 address");
        AddStat(NetStatsHost, "net.ipv6", "IPv6 address");

        AddStat(GpuStatsHost, "gpu.util", "Utilization");
        AddStat(GpuStatsHost, "gpu.dedicated", "Dedicated GPU memory");
        AddStat(GpuStatsHost, "gpu.shared", "Shared GPU memory");
        AddStat(GpuStatsHost, "gpu.driver", "Driver version");
        AddStat(GpuStatsHost, "gpu.temp", "Temperature");
    }

    private void AddStat(UniformGrid host, string key, string label)
    {
        var cell = new StackPanel { Margin = new Thickness(0, 0, 14, 18) };
        cell.Children.Add(new TextBlock
        {
            Text = label,
            Style = StyleRes("MetricLabel"),
            TextTrimming = TextTrimming.CharacterEllipsis,
        });
        var value = new TextBlock
        {
            Text = "—",
            FontSize = 19,
            Foreground = Res("TextPrimary"),
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap,
        };
        cell.Children.Add(value);
        host.Children.Add(cell);
        _stats[key] = value;
    }

    private void SetStat(string key, string text)
    {
        if (!_stats.TryGetValue(key, out var value)) return;
        value.Text = string.IsNullOrWhiteSpace(text) ? "—" : text;
    }

    private void BuildMemoryLegend()
    {
        _legendInUse = AddLegendItem("In use", Res("AccentBrush"));
        _legendCached = AddLegendItem("Cached", Res("MemLine"));
        _legendFree = AddLegendItem("Free", Res("BarEmpty"));
        _legendCompressed = AddLegendItem("Compressed", _compressedBrush);
    }

    private TextBlock AddLegendItem(string label, Brush color)
    {
        var group = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 20, 4) };
        group.Children.Add(new Border
        {
            Width = 8,
            Height = 8,
            CornerRadius = new CornerRadius(2),
            Background = color,
            VerticalAlignment = VerticalAlignment.Center,
        });
        group.Children.Add(new TextBlock
        {
            Text = label,
            Style = StyleRes("MetricLabel"),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        });
        var value = new TextBlock
        {
            Text = "",
            Style = StyleRes("Metric"),
            Margin = new Thickness(6, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
        };
        group.Children.Add(value);
        MemoryLegend.Children.Add(group);
        return value;
    }

    // ------------------------------------------------------------------ graph plumbing

    private void SetAreaSeries(AreaGraph graph, bool autoScale, params (RingBuffer Data, Brush Stroke)[] series)
    {
        graph.AutoScale = autoScale;
        graph.Series.Clear();
        foreach (var (data, stroke) in series)
        {
            graph.Series.Add(new SeriesDef
            {
                Data = data,
                Stroke = stroke,
                Scale = autoScale ? 0 : 100,
            });
        }
    }

    private Brush BrushFor(TileKind kind) => kind switch
    {
        TileKind.Cpu => Res("CpuLine"),
        TileKind.Memory => Res("MemLine"),
        TileKind.Disk => Res("DiskLine"),
        TileKind.Network => Res("NetLine"),
        TileKind.Gpu => _gpuBrush,
        _ => Brushes.DodgerBlue,
    };

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.DodgerBlue;

    private Style? StyleRes(string key) => TryFindResource(key) as Style;

    // ------------------------------------------------------------------ snapshot lookups

    private static DiskVolume? FindDisk(SystemSnapshot system, string instance)
    {
        foreach (var disk in system.Disks)
            if (string.Equals(disk.Instance, instance, StringComparison.OrdinalIgnoreCase)) return disk;
        return null;
    }

    private static NetworkAdapter? FindNetwork(SystemSnapshot system, string name)
    {
        foreach (var adapter in system.Networks)
            if (string.Equals(adapter.Name, name, StringComparison.OrdinalIgnoreCase)) return adapter;
        return null;
    }

    private static GpuAdapterInfo? GpuAt(SystemSnapshot system, int index) =>
        index >= 0 && index < system.Gpus.Count ? system.Gpus[index] : null;

    private static double MaxGpuEngine(GpuAdapterInfo gpu) =>
        Math.Max(Math.Max(gpu.Percent3D, gpu.PercentCopy), Math.Max(gpu.PercentVideoEncode, gpu.PercentVideoDecode));

    private static double Gb(ulong bytes) => bytes / 1024.0 / 1024.0 / 1024.0;

    private static string DiskTitle(DiskVolume volume)
    {
        var instance = (volume.Instance ?? "").Trim();
        var label = (volume.Label ?? "").Trim();
        int space = instance.IndexOf(' ');
        if (space < 0)
            return string.IsNullOrEmpty(label) ? (string.IsNullOrEmpty(instance) ? "Disk" : instance) : $"Disk {instance} ({label})";

        var number = instance[..space];
        if (label.Length == 0) label = instance[(space + 1)..].Trim();
        if (number.Length == 0) return string.IsNullOrEmpty(label) ? "Disk" : $"Disk ({label})";
        return string.IsNullOrEmpty(label) ? $"Disk {number}" : $"Disk {number} ({label})";
    }

    /// <summary>Core now fills Capacity/FreeSpace, but keep a DriveInfo fallback for volumes it cannot map.</summary>
    private void RefreshDiskSpace(SystemSnapshot system)
    {
        bool refreshAll = _samples % 30 == 1;
        foreach (var disk in system.Disks)
        {
            var key = disk.Instance ?? "";
            if (disk.Capacity > 0)
            {
                _diskSpace[key] = (disk.Capacity, disk.FreeSpace);
                continue;
            }
            if (_diskSpace.ContainsKey(key) && !refreshAll) continue;

            ulong capacity = 0, free = 0;
            foreach (var letter in DiskLetters(disk))
            {
                try
                {
                    var drive = new DriveInfo(letter.ToString());
                    if (!drive.IsReady) continue;
                    capacity += (ulong)drive.TotalSize;
                    free += (ulong)drive.AvailableFreeSpace;
                }
                catch
                {
                    // A volume without a letter / not ready is simply skipped.
                }
            }
            _diskSpace[key] = (capacity, free);
        }
    }

    private static IEnumerable<char> DiskLetters(DiskVolume disk)
    {
        var seen = new HashSet<char>();
        foreach (var raw in ((disk.Label ?? "") + " " + (disk.Instance ?? "")).Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var token = raw.TrimEnd(':');
            if (token.Length != 1 || !char.IsLetter(token[0])) continue;
            var letter = char.ToUpperInvariant(token[0]);
            if (seen.Add(letter)) yield return letter;
        }
    }

    private (ulong Capacity, ulong Free) DiskSpace(DiskVolume disk)
    {
        if (disk.Capacity > 0) return (disk.Capacity, disk.FreeSpace);
        return _diskSpace.TryGetValue(disk.Instance ?? "", out var cached) ? cached : (0, 0);
    }

    private static string LinkSpeedText(double bitsPerSecond)
    {
        if (bitsPerSecond <= 0) return "";
        if (bitsPerSecond >= 1_000_000_000) return $"{bitsPerSecond / 1_000_000_000:0.0} Gbps";
        if (bitsPerSecond >= 1_000_000) return $"{bitsPerSecond / 1_000_000:0} Mbps";
        if (bitsPerSecond >= 1_000) return $"{bitsPerSecond / 1_000:0} Kbps";
        return $"{bitsPerSecond:0} bps";
    }

    // ------------------------------------------------------------------ one-off environment info

    private static string ReadCpuModelName()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            var value = key?.GetValue("ProcessorNameString") as string;
            return string.IsNullOrWhiteSpace(value) ? "" : value.Trim();
        }
        catch
        {
            return "";
        }
    }

    private void LoadFirmwareMemoryInfo()
    {
        try
        {
            if (!FirmwareMemory.TryRead(out var speed, out var formFactor, out var used, out var total)) return;
            _memSpeed = speed;
            _memFormFactor = formFactor;
            _memSlotsText = total > 0 ? $"{used} of {total}" : "";
        }
        catch
        {
            // Firmware access can be denied; the page then shows em dashes.
        }
    }

    private void EnsureNetworkAddresses()
    {
        if (_networkAddressesLoaded) return;
        _networkAddressesLoaded = true;
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                string ipv4 = "", ipv6 = "";
                foreach (var unicast in nic.GetIPProperties().UnicastAddresses)
                {
                    var address = unicast.Address;
                    if (address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        if (ipv4.Length == 0 && !IPAddress.IsLoopback(address)) ipv4 = address.ToString();
                    }
                    else if (address.AddressFamily == AddressFamily.InterNetworkV6)
                    {
                        if (ipv6.Length == 0 && !IPAddress.IsLoopback(address) && !address.IsIPv6LinkLocal)
                            ipv6 = address.ToString();
                    }
                }
                if (ipv4.Length == 0 && ipv6.Length == 0) continue;

                StoreAddress(_ipv4, NormalizeNetworkName(nic.Description), ipv4);
                StoreAddress(_ipv4, NormalizeNetworkName(nic.Name), ipv4);
                StoreAddress(_ipv4, NormalizeNetworkName(nic.Id), ipv4);
                StoreAddress(_ipv6, NormalizeNetworkName(nic.Description), ipv6);
                StoreAddress(_ipv6, NormalizeNetworkName(nic.Name), ipv6);
                StoreAddress(_ipv6, NormalizeNetworkName(nic.Id), ipv6);
            }
        }
        catch
        {
            // Network information is best effort.
        }
    }

    private static void StoreAddress(Dictionary<string, string> map, string? key, string value)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return;
        map.TryAdd(key, value);
    }

    private static string LookupAddress(Dictionary<string, string> map, string name)
    {
        var key = NormalizeNetworkName(name);
        if (key.Length == 0) return "";
        if (map.TryGetValue(key, out var exact)) return exact;
        foreach (var pair in map)
        {
            if (pair.Key.Length < 6) continue;
            if (pair.Key.StartsWith(key, StringComparison.Ordinal) ||
                key.StartsWith(pair.Key, StringComparison.Ordinal))
                return pair.Value;
        }
        return "";
    }

    /// <summary>PDH sanitises adapter names ("Intel[R] Wi-Fi"); restore normal punctuation for display.</summary>
    private static string CleanAdapterName(string? name) =>
        string.IsNullOrEmpty(name) ? "" : name.Replace('[', '(').Replace(']', ')').Trim();

    /// <summary>Comparison key for adapter names (ignores punctuation/case and [R]/[TM] substitutions).</summary>
    private static string NormalizeNetworkName(string? value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            if (char.IsLetterOrDigit(ch)) builder.Append(char.ToLowerInvariant(ch));
        }
        return builder.ToString();
    }

    // ------------------------------------------------------------------ SMBIOS memory details

    /// <summary>Reads memory speed / form factor / slot usage from the SMBIOS firmware table.</summary>
    private static class FirmwareMemory
    {
        private const uint RsmbSignature = 0x52534D42; // 'RSMB'

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint GetSystemFirmwareTable(uint firmwareTableProviderSignature, uint firmwareTableId,
            byte[]? firmwareTableBuffer, uint bufferSize);

        public static bool TryRead(out string speed, out string formFactor, out int slotsUsed, out int slotsTotal)
        {
            speed = "";
            formFactor = "";
            slotsUsed = 0;
            slotsTotal = 0;

            uint size = GetSystemFirmwareTable(RsmbSignature, 0, null, 0);
            if (size < 16 || size > 4_000_000) return false;

            var buffer = new byte[size];
            if (GetSystemFirmwareTable(RsmbSignature, 0, buffer, size) != size) return false;

            int length = BitConverter.ToInt32(buffer, 4);
            int start = 8;
            int end = Math.Min(buffer.Length, start + length);
            if (end <= start) return false;

            int offset = start;
            int maxSpeed = 0;
            int form = 0;

            while (offset + 4 <= end)
            {
                byte type = buffer[offset];
                byte structureLength = buffer[offset + 1];
                if (type == 0x7F) break;
                if (structureLength < 4) break;

                if (type == 16 && offset + 15 <= end)
                {
                    slotsTotal = BitConverter.ToUInt16(buffer, offset + 0x0D);
                }
                else if (type == 17 && structureLength >= 0x15 && offset + 0x15 <= end)
                {
                    int capacity = BitConverter.ToUInt16(buffer, offset + 0x0C);
                    if (capacity == 0x7FFF && offset + 0x20 <= end)
                        capacity = (int)Math.Min(int.MaxValue, BitConverter.ToUInt32(buffer, offset + 0x1C));

                    if (capacity > 0 && capacity != 0xFFFF)
                    {
                        slotsUsed++;
                        int currentForm = buffer[offset + 0x0E];
                        if (form == 0) form = currentForm;

                        int currentSpeed = structureLength >= 0x17 && offset + 0x17 <= end
                            ? BitConverter.ToUInt16(buffer, offset + 0x15)
                            : 0;
                        if (currentSpeed == 0 && structureLength >= 0x22 && offset + 0x22 <= end)
                            currentSpeed = BitConverter.ToUInt16(buffer, offset + 0x20);
                        if (currentSpeed > maxSpeed) maxSpeed = currentSpeed;
                    }
                }

                int next = offset + structureLength;
                while (next + 1 < end && !(buffer[next] == 0 && buffer[next + 1] == 0)) next++;
                offset = next + 2;
            }

            if (maxSpeed > 0) speed = maxSpeed + " MT/s";
            formFactor = FormFactorName(form);
            if (slotsTotal > 0 && slotsUsed > slotsTotal) slotsUsed = slotsTotal;
            return speed.Length > 0 || formFactor.Length > 0 || slotsTotal > 0;
        }

        private static string FormFactorName(int formFactor) => formFactor switch
        {
            0x01 => "Other",
            0x02 => "Unknown",
            0x03 => "SIMM",
            0x04 => "SIP",
            0x05 => "Chip",
            0x06 => "DIP",
            0x07 => "ZIP",
            0x08 => "Proprietary Card",
            0x09 => "DIMM",
            0x0A => "TSOP",
            0x0B => "Row of chips",
            0x0C => "RIMM",
            0x0D => "SODIMM",
            0x0E => "SRIMM",
            0x0F => "FB-DIMM",
            0x10 => "Die",
            _ => "",
        };
    }
}