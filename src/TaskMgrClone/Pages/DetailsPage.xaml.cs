using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskMgrClone.Core;
using TaskMgrClone.UI;

namespace TaskMgrClone.Pages;

/// <summary>
/// Flat list of every running process with the full Windows Task Manager column set.
/// Rows are ProcessRow objects from the sampler wrapped in a stable item so the page can raise
/// change notifications (the sampler mutates rows in place without notifying) and carry the
/// per-tick page-fault delta.
/// </summary>
public partial class DetailsPage : PageBase
{
    private sealed class DetailsItem : Observable
    {
        public ProcessRow Row { get; set; } = null!;

        /// <summary>Page-fault delta since the previous sample.</summary>
        public ulong PfDelta { get; set; }

        public string Status => Row.Suspended ? "Suspended" : string.IsNullOrWhiteSpace(Row.Status) ? "Running" : Row.Status;

        public void NotifyAll() => Raise(string.Empty);
    }

    private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "System", "Idle", "Registry", "Memory Compression", "Secure System",
        "csrss.exe", "smss.exe", "wininit.exe", "services.exe", "lsass.exe", "winlogon.exe",
    };

    private static readonly string[] PriorityNames = { "Realtime", "High", "Above normal", "Normal", "Below normal", "Low" };

    private readonly ObservableCollection<DetailsItem> _rows = new();
    private readonly Dictionary<int, DetailsItem> _byPid = new();
    private Dictionary<int, ulong> _previousFaults = new();
    private readonly Dictionary<int, ulong> _faultDeltas = new();
    private Dictionary<int, ProcessRow>? _lastProcesses;

    private string _search = "";
    private string _sortPath = "Name";
    private bool _sortAscending = true;

    public DetailsPage()
    {
        InitializeComponent();
        ProcessGrid.ItemsSource = _rows;
        NameColumn.SortDirection = ListSortDirection.Ascending;
    }

    public override string Title => "Details";
    public override bool CanSearch => true;
    public override bool SupportsEndTask => true;
    public override string PrimaryButtonText => "End task";

    // ------------------------------------------------------------------ sampling

    public override void OnSample(SystemSnapshot system, Dictionary<int, ProcessRow> processes)
    {
        try
        {
            if (processes is null) return;
            UpdatePageFaultDeltas(processes);
            Rebuild(processes);
        }
        catch
        {
            // The grid must never take the application down; a bad tick is skipped.
        }
    }

    public override void SetSearch(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (string.Equals(normalized, _search, StringComparison.Ordinal)) return;
        _search = normalized;
        if (_lastProcesses is null) return;
        try { Rebuild(_lastProcesses); } catch { }
    }

    public override void RefreshNow()
    {
        if (_lastProcesses is null) return;
        try { Rebuild(_lastProcesses); } catch { }
    }

    private void UpdatePageFaultDeltas(Dictionary<int, ProcessRow> processes)
    {
        _faultDeltas.Clear();
        var next = new Dictionary<int, ulong>(processes.Count);
        foreach (var pair in processes)
        {
            if (pair.Key <= 0 || pair.Value is null) continue;
            var faults = pair.Value.PageFaults;
            var delta = _previousFaults.TryGetValue(pair.Key, out var previous) && faults >= previous
                ? faults - previous
                : 0UL;
            _faultDeltas[pair.Key] = delta;
            next[pair.Key] = faults;
        }
        _previousFaults = next;
    }

    private void Rebuild(Dictionary<int, ProcessRow> processes)
    {
        _lastProcesses = processes;

        var desired = new List<DetailsItem>(processes.Count);
        foreach (var pair in processes)
        {
            var process = pair.Value;
            if (process is null || pair.Key <= 0) continue;
            if (!Matches(process)) continue;

            if (!_byPid.TryGetValue(pair.Key, out var item))
            {
                item = new DetailsItem { Row = process };
                _byPid[pair.Key] = item;
            }
            else
            {
                item.Row = process;
            }

            item.PfDelta = _faultDeltas.TryGetValue(pair.Key, out var delta) ? delta : 0;
            item.NotifyAll();
            desired.Add(item);
        }

        // Forget wrappers for processes that no longer exist.
        foreach (var pid in _byPid.Keys.ToList())
            if (!processes.ContainsKey(pid))
                _byPid.Remove(pid);

        SortItems(desired);
        ListSync.Sync(_rows, desired);
        Reorder(desired);
        CountText.Text = _rows.Count == 1 ? "1 process" : $"{_rows.Count} processes";
    }

    private bool Matches(ProcessRow row)
    {
        if (_search.Length == 0) return true;
        if (!string.IsNullOrEmpty(row.Name) && row.Name.Contains(_search, StringComparison.CurrentCultureIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(row.UserName) && row.UserName.Contains(_search, StringComparison.CurrentCultureIgnoreCase)) return true;
        if (!string.IsNullOrEmpty(row.Path) && row.Path.Contains(_search, StringComparison.OrdinalIgnoreCase)) return true;
        return row.Pid.ToString().Contains(_search, StringComparison.OrdinalIgnoreCase);
    }

    // ------------------------------------------------------------------ sorting

    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var path = e.Column.SortMemberPath;
        if (string.IsNullOrEmpty(path)) return;

        if (string.Equals(_sortPath, path, StringComparison.Ordinal))
        {
            _sortAscending = !_sortAscending;
        }
        else
        {
            _sortPath = path;
            _sortAscending = true;
        }

        foreach (var column in ProcessGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = _sortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;

        if (_lastProcesses is null) return;
        try { Rebuild(_lastProcesses); } catch { }
    }

    private void SortItems(List<DetailsItem> items)
    {
        var path = _sortPath;
        var ascending = _sortAscending;
        items.Sort((a, b) =>
        {
            var result = Compare(a, b, path);
            if (result == 0) result = a.Row.Pid.CompareTo(b.Row.Pid);
            return ascending ? result : -result;
        });
    }

    private static int Compare(DetailsItem a, DetailsItem b, string path)
    {
        switch (path)
        {
            case "Pid": return a.Row.Pid.CompareTo(b.Row.Pid);
            case "Status": return string.Compare(a.Status, b.Status, StringComparison.CurrentCultureIgnoreCase);
            case "User": return string.Compare(a.Row.UserName, b.Row.UserName, StringComparison.CurrentCultureIgnoreCase);
            case "Cpu": return a.Row.CpuPercent.CompareTo(b.Row.CpuPercent);
            case "Memory": return a.Row.WorkingSetBytes.CompareTo(b.Row.WorkingSetBytes);
            case "Commit": return a.Row.CommitBytes.CompareTo(b.Row.CommitBytes);
            case "PagedPool": return a.Row.PagedPoolBytes.CompareTo(b.Row.PagedPoolBytes);
            case "NonPagedPool": return a.Row.NonPagedPoolBytes.CompareTo(b.Row.NonPagedPoolBytes);
            case "PageFaults": return a.Row.PageFaults.CompareTo(b.Row.PageFaults);
            case "PfDelta": return a.PfDelta.CompareTo(b.PfDelta);
            case "BasePriority": return a.Row.BasePriority.CompareTo(b.Row.BasePriority);
            case "Handles": return a.Row.HandleCount.CompareTo(b.Row.HandleCount);
            case "Threads": return a.Row.ThreadCount.CompareTo(b.Row.ThreadCount);
            case "UserObjects": return a.Row.UserObjects.CompareTo(b.Row.UserObjects);
            case "GdiObjects": return a.Row.GdiObjects.CompareTo(b.Row.GdiObjects);
            case "IoRead": return a.Row.IoReadBytes.CompareTo(b.Row.IoReadBytes);
            case "IoWrite": return a.Row.IoWriteBytes.CompareTo(b.Row.IoWriteBytes);
            case "IoOther": return a.Row.IoOtherBytes.CompareTo(b.Row.IoOtherBytes);
            case "Disk": return a.Row.DiskBytesPerSec.CompareTo(b.Row.DiskBytesPerSec);
            case "Gpu": return a.Row.GpuPercent.CompareTo(b.Row.GpuPercent);
            case "GpuMemory": return a.Row.GpuDedicatedBytes.CompareTo(b.Row.GpuDedicatedBytes);
            case "Power": return Nullable.Compare(a.Row.PowerUsage, b.Row.PowerUsage);
            case "PowerTrend": return a.Row.PowerUsageTrend.CompareTo(b.Row.PowerUsageTrend);
            default: return string.Compare(a.Row.Name, b.Row.Name, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    /// <summary>Moves existing row objects into the requested display order (keeps selection alive).</summary>
    private void Reorder(IReadOnlyList<DetailsItem> desired)
    {
        var count = Math.Min(desired.Count, _rows.Count);
        for (int i = 0; i < count; i++)
        {
            if (ReferenceEquals(_rows[i], desired[i])) continue;
            var from = -1;
            for (int j = i + 1; j < _rows.Count; j++)
            {
                if (!ReferenceEquals(_rows[j], desired[i])) continue;
                from = j;
                break;
            }
            if (from < 0) continue;
            _rows.Move(from, i);
        }
    }

    // ------------------------------------------------------------------ selection help

    private List<ProcessRow> SelectedRows()
    {
        var rows = new List<ProcessRow>();
        foreach (var item in ProcessGrid.SelectedItems)
            if (item is DetailsItem details && details.Row is not null)
                rows.Add(details.Row);
        return rows;
    }

    private ProcessRow? PrimaryRow() => SelectedRows().FirstOrDefault();

    private void OnGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.OriginalSource is not DependencyObject source) return;
            var row = FindAncestor<DataGridRow>(source);
            if (row is null || row.IsSelected) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) ProcessGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
        catch { /* selection is cosmetic */ }
    }

    private void OnGridMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.OriginalSource is not DependencyObject source) return;
            if (FindAncestor<DataGridRow>(source) is null) return;
            ShowProperties();
            e.Handled = true;
        }
        catch { }
    }

    private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
    {
        while (current is not null)
        {
            if (current is T match) return match;
            current = ParentOf(current);
        }
        return null;
    }

    private static DependencyObject? ParentOf(DependencyObject current)
    {
        try
        {
            return current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        catch
        {
            return null;
        }
    }

    // ------------------------------------------------------------------ context menu

    private void OnContextMenuOpened(object sender, RoutedEventArgs e)
    {
        var rows = SelectedRows();
        var any = rows.Count > 0;
        EndTaskMenu.IsEnabled = any;
        EndTreeMenu.IsEnabled = any;
        RestartMenu.IsEnabled = any;
        PriorityMenu.IsEnabled = any;
        AffinityMenu.IsEnabled = any;
        DumpMenu.IsEnabled = any;
        OnlineMenu.IsEnabled = any;
        PropertiesMenu.IsEnabled = any;
        LocationMenu.IsEnabled = any && !string.IsNullOrWhiteSpace(rows[0].Path);

        var current = any ? rows[0].Priority ?? string.Empty : string.Empty;
        PriorityRealtime.IsChecked = IsPriority(current, "Realtime");
        PriorityHigh.IsChecked = IsPriority(current, "High");
        PriorityAboveNormal.IsChecked = IsPriority(current, "Above normal");
        PriorityNormal.IsChecked = IsPriority(current, "Normal");
        PriorityBelowNormal.IsChecked = IsPriority(current, "Below normal");
        PriorityLow.IsChecked = IsPriority(current, "Low");
    }

    private static bool IsPriority(string current, string name) =>
        !string.IsNullOrEmpty(current) && string.Equals(current, name, StringComparison.OrdinalIgnoreCase);

    private static bool IsCritical(ProcessRow row) =>
        row.Pid is 0 or 4 || CriticalNames.Contains(row.Name ?? string.Empty);

    private void OnEndTaskMenu(object sender, RoutedEventArgs e) => EndSelected(false);

    private void OnEndTreeMenu(object sender, RoutedEventArgs e) => EndSelected(true);

    private void EndSelected(bool tree)
    {
        try
        {
            var rows = SelectedRows();
            if (rows.Count == 0) return;
            var blocked = false;
            foreach (var row in rows)
            {
                if (IsCritical(row)) { blocked = true; continue; }
                var ok = tree ? KillTree(row.Pid) : ProcessActions.EndTaskGraceful(row.Pid);
                if (!ok) Warn($"Unable to end \"{row.Name}\" (PID {row.Pid}). Access is denied.");
            }
            if (blocked) Warn("This is a critical system process. Task Manager cannot end it.");
        }
        catch
        {
            Warn("The operation could not be completed.");
        }
    }

    private static bool KillTree(int pid) => KillTree(pid, new HashSet<int>());

    private static bool KillTree(int pid, HashSet<int> visited)
    {
        if (!visited.Add(pid)) return false;
        foreach (var child in ProcessActions.Children(pid).ToList())
            KillTree(child, visited);
        return ProcessActions.Kill(pid);
    }

    private void OnRestartMenu(object sender, RoutedEventArgs e)
    {
        try
        {
            var rows = SelectedRows();
            if (rows.Count == 0) return;
            var blocked = false;
            foreach (var row in rows)
            {
                if (IsCritical(row)) { blocked = true; continue; }
                if (!ProcessActions.Restart(row.Pid))
                    Warn($"Unable to restart \"{row.Name}\" (PID {row.Pid}). Access is denied.");
            }
            if (blocked) Warn("This is a critical system process. Task Manager cannot restart it.");
        }
        catch
        {
            Warn("The operation could not be completed.");
        }
    }

    private void OnSetPriority(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not MenuItem item || item.Tag is not string name) return;
            if (!ProcessActions.TryParsePriority(name, out var priorityClass)) return;
            foreach (var row in SelectedRows())
            {
                if (IsCritical(row)) continue;
                if (!ProcessActions.SetPriority(row.Pid, priorityClass))
                    Warn($"Unable to change the priority of \"{row.Name}\" (PID {row.Pid}). Access is denied.");
            }
        }
        catch
        {
            Warn("The priority could not be changed.");
        }
    }

    private void OnSetAffinityMenu(object sender, RoutedEventArgs e)
    {
        var row = PrimaryRow();
        if (row is null) return;
        try
        {
            var window = new AffinityWindow(row);
            var owner = Window.GetWindow(this);
            if (owner is not null) window.Owner = owner;
            window.ShowDialog();
        }
        catch
        {
            Warn("The processor affinity could not be changed.");
        }
    }

    private void OnCreateDumpMenu(object sender, RoutedEventArgs e)
    {
        var rows = SelectedRows();
        if (rows.Count == 0) return;

        var created = new List<string>();
        var failed = new List<string>();
        foreach (var row in rows)
        {
            try
            {
                var name = SanitizeFileName(row.Name);
                if (name.Length == 0) name = "process";
                var path = Path.Combine(Path.GetTempPath(), $"{name}_{row.Pid}.dmp");
                if (ProcessActions.CreateDump(row.Pid, path)) created.Add(path);
                else failed.Add(row.Name);
            }
            catch
            {
                failed.Add(row.Name);
            }
        }

        if (created.Count == 0)
        {
            Warn("The dump file could not be created. Access is denied.");
            return;
        }

        var message = "Dump file" + (created.Count == 1 ? "" : "s") + " created:\n" + string.Join("\n", created);
        if (failed.Count > 0)
            message += "\n\nCould not create a dump for: " + string.Join(", ", failed);
        Info(message);
    }

    private static string SanitizeFileName(string? name)
    {
        if (string.IsNullOrEmpty(name)) return string.Empty;
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Where(c => !invalid.Contains(c)).ToArray()).Trim();
    }

    private void OnOpenFileLocationMenu(object sender, RoutedEventArgs e)
    {
        var row = PrimaryRow();
        if (row is null) return;
        if (string.IsNullOrWhiteSpace(row.Path))
        {
            Warn("The file location for this process is not available.");
            return;
        }
        try
        {
            Process.Start("explorer.exe", "/select,\"" + row.Path + "\"");
        }
        catch
        {
            Warn("The file location could not be opened.");
        }
    }

    private void OnSearchOnlineMenu(object sender, RoutedEventArgs e)
    {
        var row = PrimaryRow();
        if (row is null) return;
        try
        {
            Process.Start(new ProcessStartInfo("https://www.bing.com/search?q=" + Uri.EscapeDataString(row.Name ?? string.Empty))
            {
                UseShellExecute = true,
            });
        }
        catch
        {
            Warn("The search could not be opened in the browser.");
        }
    }

    private void OnPropertiesMenu(object sender, RoutedEventArgs e) => ShowProperties();

    private void ShowProperties()
    {
        var row = PrimaryRow();
        if (row is null) return;
        try
        {
            var window = new PropertiesWindow(row);
            var owner = Window.GetWindow(this);
            if (owner is not null) window.Owner = owner;
            window.ShowDialog();
        }
        catch
        {
            Warn("The properties dialog could not be opened.");
        }
    }

    // ------------------------------------------------------------------ command bar

    public override void OnPrimaryAction() => EndSelected(false);

    private void Warn(string message)
    {
        var owner = Window.GetWindow(this);
        if (owner is not null)
            MessageBox.Show(owner, message, "Task Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(message, "Task Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void Info(string message)
    {
        var owner = Window.GetWindow(this);
        if (owner is not null)
            MessageBox.Show(owner, message, "Task Manager", MessageBoxButton.OK, MessageBoxImage.Information);
        else
            MessageBox.Show(message, "Task Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }
}