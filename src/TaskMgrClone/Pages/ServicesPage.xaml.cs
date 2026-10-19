using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using TaskMgrClone.Core;
using TaskMgrClone.UI;

namespace TaskMgrClone.Pages;

/// <summary>
/// Windows services list. Enumeration is heavier than the 1 Hz sample, so the page refreshes
/// its cached enumeration every other tick (~2 s) and reconciles rows by service name.
/// </summary>
public partial class ServicesPage : PageBase
{
    private sealed class ServiceItem : Observable
    {
        public ServiceRow Service { get; set; } = new();
        public string Name => Service.Name;
        public int Pid => Service.Pid;
        public string Description => Service.Description;
        public string Status => Service.Status;
        public string Group => Service.Group;

        public void NotifyAll() => Raise(string.Empty);
    }

    private readonly ObservableCollection<ServiceItem> _rows = new();
    private List<ServiceRow> _lastServices = new();
    private string _search = "";
    private string _sortPath = "Name";
    private bool _sortAscending = true;
    private bool _refreshing;
    private int _ticks;

    public ServicesPage()
    {
        InitializeComponent();
        ServiceGrid.ItemsSource = _rows;
        NameColumn.SortDirection = ListSortDirection.Ascending;
    }

    public override string Title => "Services";
    public override bool CanSearch => true;

    // ------------------------------------------------------------------ refresh

    public override void OnSample(SystemSnapshot system, Dictionary<int, ProcessRow> processes)
    {
        // First sample populates immediately, then every second tick keeps the list ~2 s fresh.
        if (++_ticks % 2 != 1) return;
        _ = RefreshAsync();
    }

    public override void RefreshNow() => _ = RefreshAsync();

    public override void SetSearch(string text)
    {
        var normalized = (text ?? string.Empty).Trim();
        if (string.Equals(normalized, _search, StringComparison.Ordinal)) return;
        _search = normalized;
        try { Apply(_lastServices); } catch { }
    }

    private async Task RefreshAsync()
    {
        if (_refreshing) return;
        _refreshing = true;
        try
        {
            var services = await Task.Run(ServiceControl.Enumerate);
            if (Dispatcher.CheckAccess())
                Apply(services);
            else
                Dispatcher.Invoke(() => Apply(services));
        }
        catch
        {
            // Enumeration is best effort; keep showing the previous list.
        }
        finally
        {
            _refreshing = false;
        }
    }

    private void Apply(List<ServiceRow> services)
    {
        _lastServices = services ?? new List<ServiceRow>();

        var fresh = new List<ServiceItem>(_lastServices.Count);
        foreach (var service in _lastServices)
        {
            if (service is null || string.IsNullOrEmpty(service.Name)) continue;
            if (!Matches(service)) continue;
            fresh.Add(new ServiceItem { Service = service });
        }

        SortItems(fresh);
        ListSync.SyncByKey(_rows, fresh, item => item.Name, (existing, incoming) =>
        {
            existing.Service = incoming.Service;
            existing.NotifyAll();
        });

        // SyncByKey preserves existing row objects, so map the sorted order back onto them.
        var byName = new Dictionary<string, ServiceItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in _rows) byName[row.Name] = row;
        var desired = new List<ServiceItem>(fresh.Count);
        foreach (var item in fresh)
            if (byName.TryGetValue(item.Name, out var existing))
                desired.Add(existing);
        Reorder(desired);

        CountText.Text = _rows.Count == 1 ? "1 service" : $"{_rows.Count} services";
    }

    private bool Matches(ServiceRow service) =>
        _search.Length == 0
        || Contains(service.Name)
        || Contains(service.DisplayName)
        || Contains(service.Description)
        || Contains(service.Group);

    private bool Contains(string? text) =>
        !string.IsNullOrEmpty(text) && text.Contains(_search, StringComparison.CurrentCultureIgnoreCase);

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

        foreach (var column in ServiceGrid.Columns) column.SortDirection = null;
        e.Column.SortDirection = _sortAscending ? ListSortDirection.Ascending : ListSortDirection.Descending;

        try { Apply(_lastServices); } catch { }
    }

    private void SortItems(List<ServiceItem> items)
    {
        var path = _sortPath;
        var ascending = _sortAscending;
        items.Sort((a, b) =>
        {
            var result = Compare(a, b, path);
            if (result == 0) result = string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
            return ascending ? result : -result;
        });
    }

    private static int Compare(ServiceItem a, ServiceItem b, string path)
    {
        switch (path)
        {
            case "Pid": return a.Pid.CompareTo(b.Pid);
            case "Description": return string.Compare(a.Description, b.Description, StringComparison.CurrentCultureIgnoreCase);
            case "Status": return string.Compare(a.Status, b.Status, StringComparison.CurrentCultureIgnoreCase);
            case "Group": return string.Compare(a.Group, b.Group, StringComparison.CurrentCultureIgnoreCase);
            default: return string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase);
        }
    }

    private void Reorder(IReadOnlyList<ServiceItem> desired)
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

    private List<ServiceRow> SelectedRows()
    {
        var rows = new List<ServiceRow>();
        foreach (var item in ServiceGrid.SelectedItems)
            if (item is ServiceItem service && service.Service is not null)
                rows.Add(service.Service);
        return rows;
    }

    private void OnGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (e.OriginalSource is not DependencyObject source) return;
            var row = FindAncestor<DataGridRow>(source);
            if (row is null || row.IsSelected) return;
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0) ServiceGrid.SelectedItems.Clear();
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
            OpenServicesConsole();
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
        StartMenu.IsEnabled = rows.Any(row => !string.Equals(row.Status, "Running", StringComparison.OrdinalIgnoreCase));
        StopMenu.IsEnabled = rows.Any(row => string.Equals(row.Status, "Running", StringComparison.OrdinalIgnoreCase));
        RestartMenu.IsEnabled = rows.Any(row => string.Equals(row.Status, "Running", StringComparison.OrdinalIgnoreCase));
    }

    private void OnStartMenu(object sender, RoutedEventArgs e)
    {
        var failures = new List<ServiceRow>();
        foreach (var row in SelectedRows().Where(row => !string.Equals(row.Status, "Running", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (!ServiceControl.Start(row.Name)) failures.Add(row);
            }
            catch
            {
                failures.Add(row);
            }
        }
        ReportFailures(failures, "started");
        _ = RefreshAsync();
    }

    private void OnStopMenu(object sender, RoutedEventArgs e)
    {
        var failures = new List<ServiceRow>();
        foreach (var row in SelectedRows().Where(row => string.Equals(row.Status, "Running", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (!ServiceControl.Stop(row.Name)) failures.Add(row);
            }
            catch
            {
                failures.Add(row);
            }
        }
        ReportFailures(failures, "stopped");
        _ = RefreshAsync();
    }

    private void OnRestartMenu(object sender, RoutedEventArgs e)
    {
        var failures = new List<ServiceRow>();
        foreach (var row in SelectedRows().Where(row => string.Equals(row.Status, "Running", StringComparison.OrdinalIgnoreCase)))
        {
            try
            {
                if (!ServiceControl.Restart(row.Name)) failures.Add(row);
            }
            catch
            {
                failures.Add(row);
            }
        }
        ReportFailures(failures, "restarted");
        _ = RefreshAsync();
    }

    private void ReportFailures(List<ServiceRow> failures, string verb)
    {
        if (failures.Count == 0) return;
        var names = string.Join("\n", failures.Select(row =>
            string.IsNullOrEmpty(row.DisplayName) ? row.Name : $"{row.DisplayName} ({row.Name})"));
        Warn($"The following service(s) could not be {verb}:\n{names}\n\nYou may need to run Task Manager as an administrator.");
    }

    private void OnOpenServicesMenu(object sender, RoutedEventArgs e) => OpenServicesConsole();

    private void OpenServicesConsole()
    {
        try
        {
            Win32Ext.ShellOpen("services.msc");
        }
        catch
        {
            Warn("Services could not be opened.");
        }
    }

    private void Warn(string message)
    {
        var owner = Window.GetWindow(this);
        if (owner is not null)
            MessageBox.Show(owner, message, "Task Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        else
            MessageBox.Show(message, "Task Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
