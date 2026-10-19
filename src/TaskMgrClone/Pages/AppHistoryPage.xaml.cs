using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using TaskMgrClone.Controls;
using TaskMgrClone.Core;
using TaskMgrClone.UI;

namespace TaskMgrClone.Pages;

public partial class AppHistoryPage : PageBase
{
    private readonly ObservableCollection<AppHistoryPageRow> _rows = new();
    private Dictionary<int, ProcessRow> _processes = new();
    private string _search = "";

    public AppHistoryPage()
    {
        InitializeComponent();
        HistoryGrid.ItemsSource = _rows;
    }

    public override string Title => "App history";
    public override string PrimaryButtonText => "Delete usage history";

    public override void OnSample(SystemSnapshot system, Dictionary<int, ProcessRow> processes)
    {
        _processes = processes ?? new Dictionary<int, ProcessRow>();
        try { Rebuild(); } catch { /* a page must never throw while sampling */ }
    }

    public override void SetSearch(string text)
    {
        _search = (text ?? "").Trim();
        try { Rebuild(); } catch { }
    }

    public override void RefreshNow()
    {
        try { Rebuild(); } catch { }
    }

    public override void OnPrimaryAction()
    {
        if (!Confirm("Delete the app usage history for all apps?\n\nThis can't be undone.", "Delete usage history")) return;
        try { App.History.DeleteAll(); } catch { }
        try { Rebuild(); } catch { }
    }

    // ------------------------------------------------------------------ data

    private void Rebuild()
    {
        var live = LiveProcessInfo(_processes);
        var fresh = new List<AppHistoryPageRow>();
        foreach (var source in App.History.Rows(_processes))
        {
            if (!Matches(source)) continue;
            var row = new AppHistoryPageRow();
            row.Load(source);
            if (live.TryGetValue(row.Name, out var info))
            {
                row.IconPath = info.Path;
                if (string.IsNullOrEmpty(row.Publisher)) row.Publisher = info.Publisher;
            }
            fresh.Add(row);
        }

        ListSync.SyncByKey(_rows, fresh, r => r.Name, (target, source) => target.CopyFrom(source));
        EmptyPanel.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool Matches(AppHistoryRow row)
    {
        if (_search.Length == 0) return true;
        return row.Name.Contains(_search, StringComparison.OrdinalIgnoreCase)
            || row.Publisher.Contains(_search, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Maps the display name the store uses back to the live process image path.</summary>
    private static Dictionary<string, (string Path, string Publisher)> LiveProcessInfo(Dictionary<int, ProcessRow> processes)
    {
        var map = new Dictionary<string, (string Path, string Publisher)>(StringComparer.OrdinalIgnoreCase);
        if (processes == null) return map;
        foreach (var process in processes.Values)
        {
            if (!process.IsPackaged && !process.HasWindow) continue;
            var name = process.IsPackaged && !string.IsNullOrEmpty(process.Description)
                ? process.Description
                : Path.GetFileNameWithoutExtension(process.Name);
            if (string.IsNullOrEmpty(name)) continue;
            map[name] = (process.Path ?? "", process.Publisher ?? "");
        }
        return map;
    }

    // ------------------------------------------------------------------ commands

    private void OnDeleteSelected(object sender, RoutedEventArgs e)
    {
        try
        {
            if (HistoryGrid.SelectedItems.Count != 1) return;
            if (HistoryGrid.SelectedItem is not AppHistoryPageRow row) return;
            if (row.Source is { } source) App.History.Delete(source);
            Rebuild();
        }
        catch { }
    }

    private bool Confirm(string message, string title)
    {
        var owner = Window.GetWindow(this);
        return owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}

/// <summary>Row view model for the App history table (notifies in place so live values refresh).</summary>
public sealed class AppHistoryPageRow : Observable
{
    private string _name = "";
    private string _publisher = "";
    private string _iconPath = "";
    private TimeSpan _cpuTime;
    private ulong _networkBytes;
    private ulong _tileUpdates;
    private string _cpuTimeText = "";
    private string _networkText = "";
    private string _tileUpdatesText = "-";

    public AppHistoryRow? Source { get; private set; }

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Publisher { get => _publisher; set => Set(ref _publisher, value); }
    public string IconPath { get => _iconPath; set => Set(ref _iconPath, value); }
    public TimeSpan CpuTime { get => _cpuTime; private set => Set(ref _cpuTime, value); }
    public ulong NetworkBytes { get => _networkBytes; private set => Set(ref _networkBytes, value); }
    public ulong TileUpdates { get => _tileUpdates; private set => Set(ref _tileUpdates, value); }

    public string CpuTimeText { get => _cpuTimeText; private set => Set(ref _cpuTimeText, value); }
    public string NetworkText { get => _networkText; private set => Set(ref _networkText, value); }
    public string TileUpdatesText { get => _tileUpdatesText; private set => Set(ref _tileUpdatesText, value); }

    public void Load(AppHistoryRow source)
    {
        Source = source;
        Name = source.Name;
        Publisher = source.Publisher;
        CpuTime = source.CpuTime;
        NetworkBytes = source.NetworkBytes;
        TileUpdates = source.TileUpdates;
        CpuTimeText = Format.Duration(source.CpuTime);
        NetworkText = Format.Bytes(source.NetworkBytes);
        TileUpdatesText = source.TileUpdates == 0 ? "-" : source.TileUpdates.ToString();
    }

    public void CopyFrom(AppHistoryPageRow other)
    {
        Source = other.Source;
        Name = other.Name;
        Publisher = other.Publisher;
        IconPath = other.IconPath;
        CpuTime = other.CpuTime;
        NetworkBytes = other.NetworkBytes;
        TileUpdates = other.TileUpdates;
        CpuTimeText = other.CpuTimeText;
        NetworkText = other.NetworkText;
        TileUpdatesText = other.TileUpdatesText;
    }
}
