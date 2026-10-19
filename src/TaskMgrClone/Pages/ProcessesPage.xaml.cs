using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using TaskMgrClone.Core;
using TaskMgrClone.UI;

namespace TaskMgrClone.Pages;

public partial class ProcessesPage : PageBase
{
    /// <summary>Processes Task Manager never terminates (plus anything at PID 4 or below).</summary>
    private static readonly HashSet<string> CriticalNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "system", "idle", "csrss", "wininit", "winlogon", "services", "lsass", "smss", "dwm", "registry",
    };

    private readonly ObservableCollection<ProcessRow> _rows = new();
    private readonly ICollectionView _view;
    private readonly Dictionary<int, ProcessGroup> _groupCache = new();
    private readonly HashSet<ProcessGroup> _presentGroups = new();
    private readonly HashSet<ProcessGroup> _collapsedGroups = new();

    private Dictionary<int, ProcessRow>? _lastProcesses;
    private string _search = "";
    private string _sortPath = nameof(ProcessRow.Name);
    private bool _sortAscending = true;
    private bool _rightClickOnRow;
    private bool _mouseRightClickSeen;

    public override string Title => "Processes";
    public override bool SupportsEndTask => true;
    public override bool SupportsEfficiencyMode => true;
    public override string PrimaryButtonText => "End task";

    public ProcessesPage()
    {
        InitializeComponent();

        var source = new CollectionViewSource { Source = _rows };
        source.GroupDescriptions.Add(new PropertyGroupDescription(nameof(ProcessRow.Group)));
        _view = source.View;
        _view.Filter = FilterRow;
        _view.SortDescriptions.Add(new SortDescription(nameof(ProcessRow.Group), ListSortDirection.Ascending));
        _view.SortDescriptions.Add(new SortDescription(nameof(ProcessRow.Name), ListSortDirection.Ascending));
        ProcessGrid.ItemsSource = _view;
        NameColumn.SortDirection = ListSortDirection.Ascending;

        // Keep the per-group expand/collapse state in the page (group containers are recreated on refresh).
        ProcessGrid.AddHandler(Expander.ExpandedEvent, new RoutedEventHandler(OnAnyGroupExpanded), true);
        ProcessGrid.AddHandler(Expander.CollapsedEvent, new RoutedEventHandler(OnAnyGroupCollapsed), true);

        // Star column sizing is broken in a grouped DataGrid, so the Name column is sized by hand
        // to fill the viewport while the other six columns keep their fixed widths.
        ProcessGrid.SizeChanged += (_, _) => UpdateNameColumnWidth();
        Loaded += (_, _) => Dispatcher.BeginInvoke(new Action(UpdateNameColumnWidth), DispatcherPriority.Loaded);
    }

    private void UpdateNameColumnWidth()
    {
        try
        {
            double viewport = ProcessGrid.ActualWidth;
            if (viewport <= 0) return;

            double fixedWidth = 0;
            foreach (var column in ProcessGrid.Columns)
            {
                if (ReferenceEquals(column, NameColumn)) continue;
                fixedWidth += column.Width.UnitType == DataGridLengthUnitType.Pixel
                    ? column.Width.Value
                    : (column.ActualWidth > 0 ? column.ActualWidth : 80);
            }

            // Reserve room for the vertical scrollbar so the fixed columns never trigger a
            // horizontal scrollbar just because the process list started scrolling.
            double width = Math.Max(220, Math.Round(viewport - fixedWidth - 14));
            if (Math.Abs(NameColumn.Width.Value - width) > 0.5 ||
                NameColumn.Width.UnitType != DataGridLengthUnitType.Pixel)
                NameColumn.Width = new DataGridLength(width, DataGridLengthUnitType.Pixel);
        }
        catch { /* layout maths must never take the page down */ }
    }

    // ------------------------------------------------------------------ page contract

    public override void OnSample(SystemSnapshot system, Dictionary<int, ProcessRow> processes)
    {
        _lastProcesses = processes;
        ApplySnapshot(processes);
    }

    public override void SetSearch(string text)
    {
        _search = (text ?? "").Trim();
        RefreshView();
        UpdateFooter();
    }

    public override void RefreshNow()
    {
        if (_lastProcesses is not null) ApplySnapshot(_lastProcesses);
    }

    // ------------------------------------------------------------------ sampling

    private void ApplySnapshot(Dictionary<int, ProcessRow>? processes)
    {
        try
        {
            var desired = new List<ProcessRow>(processes?.Count ?? 0);
            bool topologyChanged = false;

            if (processes is not null)
            {
                foreach (var row in processes.Values)
                {
                    if (row is null || row.Pid <= 0) continue;
                    desired.Add(row);

                    if (_groupCache.TryGetValue(row.Pid, out var previous))
                    {
                        if (previous != row.Group) topologyChanged = true;
                    }
                    else if (!_presentGroups.Contains(row.Group))
                    {
                        // A brand new group needs the collection view rebuilt so the group
                        // headers keep the Apps / Background / Windows order.
                        topologyChanged = true;
                    }
                }
            }

            ListSync.Sync(_rows, desired);

            _groupCache.Clear();
            _presentGroups.Clear();
            foreach (var row in _rows)
            {
                _groupCache[row.Pid] = row.Group;
                _presentGroups.Add(row.Group);
            }

            if (topologyChanged) RefreshView();
            else PumpCellValues();

            UpdateFooter();
        }
        catch { /* sampling must never take the page down */ }
    }

    /// <summary>
    /// ProcessRow is shared with the sampler, which writes the sampled values straight into the
    /// object without raising change notifications. Rather than rebuilding the grid every tick
    /// (which would drop selection, focus and group expansion), re-evaluate the bindings of the
    /// realised cell visuals so they pick up the current values.
    /// </summary>
    private void PumpCellValues()
    {
        try
        {
            RefreshBindings(ProcessGrid, 0);
            ApplyGroupStates();
        }
        catch { /* cosmetic update must never throw */ }
    }

    /// <summary>Re-applies the expanded/collapsed state to the realised group headers.</summary>
    private void ApplyGroupStates()
    {
        foreach (var item in EnumerateGroupItems(ProcessGrid))
        {
            if (item.DataContext is not CollectionViewGroup group || !TryGetGroup(group, out var key)) continue;
            var expander = FindGroupExpander(item);
            if (expander is null) continue;
            bool expanded = !_collapsedGroups.Contains(key);
            if (expander.IsExpanded != expanded) expander.IsExpanded = expanded;
        }
    }

    private static void RefreshBindings(DependencyObject? node, int depth)
    {
        if (node is null || depth > 80) return;
        try
        {
            if (node is TextBlock) UpdateBinding(node, TextBlock.TextProperty);
            else if (node is Border) UpdateBinding(node, Border.VisibilityProperty);
            else if (node is Image) UpdateBinding(node, Image.SourceProperty);

            int count = VisualTreeHelper.GetChildrenCount(node);
            for (int i = 0; i < count; i++)
                RefreshBindings(VisualTreeHelper.GetChild(node, i), depth + 1);
        }
        catch { /* not every visual child is usable for tree walking */ }
    }

    private static void UpdateBinding(DependencyObject node, DependencyProperty property)
    {
        try { BindingOperations.GetBindingExpressionBase(node, property)?.UpdateTarget(); }
        catch { }
    }

    private bool FilterRow(object item)
    {
        if (item is not ProcessRow row) return true;
        if (_search.Length == 0) return true;

        bool Contains(string? value) =>
            !string.IsNullOrEmpty(value) && value.Contains(_search, StringComparison.CurrentCultureIgnoreCase);

        return Contains(row.Name) || Contains(row.UserName) || Contains(row.Publisher);
    }

    private void UpdateFooter()
    {
        int total = _rows.Count;
        int visible = total;
        try
        {
            if (_search.Length > 0)
            {
                visible = 0;
                foreach (var item in _view)
                    if (item is ProcessRow) visible++;
            }
        }
        catch { }

        if (visible == total)
            CountText.Text = total == 1 ? "1 process" : $"{total} processes";
        else
            CountText.Text = $"{visible} of {total} processes";
    }

    // ------------------------------------------------------------------ view refresh (sort / search / group change)

    private void RefreshView()
    {
        try
        {
            var selected = SelectedRows();
            _view.Refresh();
            RestoreSelection(selected);
        }
        catch { }

        ScheduleGroupStateRestore();
        PumpCellValues();
    }

    private void RestoreSelection(List<ProcessRow> selected)
    {
        if (selected.Count == 0) return;
        try
        {
            ProcessGrid.SelectedItems.Clear();
            foreach (var row in selected)
                if (_view.Contains(row)) ProcessGrid.SelectedItems.Add(row);
        }
        catch { /* selection restore is best effort */ }
    }

    private void ScheduleGroupStateRestore()
    {
        try
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try { ApplyGroupStates(); }
                catch { }
            }), DispatcherPriority.Loaded);
        }
        catch { }
    }

    // ------------------------------------------------------------------ group headers

    private void OnGroupItemLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is not GroupItem item ||
            item.DataContext is not CollectionViewGroup group ||
            !TryGetGroup(group, out var key)) return;

        var expander = FindGroupExpander(item);
        if (expander is null) return;
        bool expanded = !_collapsedGroups.Contains(key);
        if (expander.IsExpanded != expanded) expander.IsExpanded = expanded;
    }

    private void OnAnyGroupExpanded(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Expander { DataContext: CollectionViewGroup group } &&
            TryGetGroup(group, out var key))
            _collapsedGroups.Remove(key);
    }

    private void OnAnyGroupCollapsed(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is Expander { DataContext: CollectionViewGroup group } &&
            TryGetGroup(group, out var key))
            _collapsedGroups.Add(key);
    }

    private static bool TryGetGroup(CollectionViewGroup group, out ProcessGroup key)
    {
        if (group.Name is ProcessGroup value)
        {
            key = value;
            return true;
        }
        key = default;
        return false;
    }

    private static Expander? FindGroupExpander(GroupItem item)
    {
        try
        {
            if (item.Template?.FindName("GroupHeaderExpander", item) is Expander named) return named;
        }
        catch { }
        return FindDescendant<Expander>(item);
    }

    private static IEnumerable<GroupItem> EnumerateGroupItems(DependencyObject root)
    {
        var found = new List<GroupItem>();
        CollectGroupItems(root, found, 0);
        return found;
    }

    private static void CollectGroupItems(DependencyObject node, List<GroupItem> found, int depth)
    {
        if (depth > 80) return;
        int count;
        try { count = VisualTreeHelper.GetChildrenCount(node); } catch { return; }
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is GroupItem item) found.Add(item);
            CollectGroupItems(child, found, depth + 1);
        }
    }

    private static T? FindDescendant<T>(DependencyObject? node) where T : DependencyObject
    {
        if (node is null) return null;
        int count;
        try { count = VisualTreeHelper.GetChildrenCount(node); } catch { return null; }
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(node, i);
            if (child is T match) return match;
            var nested = FindDescendant<T>(child);
            if (nested is not null) return nested;
        }
        return null;
    }

    // ------------------------------------------------------------------ sorting

    private void OnSorting(object sender, DataGridSortingEventArgs e)
    {
        e.Handled = true;
        var column = e.Column;
        if (column is null) return;

        bool ascending = column.SortDirection != ListSortDirection.Ascending;
        foreach (var other in ProcessGrid.Columns) other.SortDirection = null;
        column.SortDirection = ascending ? ListSortDirection.Ascending : ListSortDirection.Descending;

        string path = string.IsNullOrEmpty(column.SortMemberPath) ? nameof(ProcessRow.Name) : column.SortMemberPath;
        _sortPath = path;
        _sortAscending = ascending;

        try
        {
            using (_view.DeferRefresh())
            {
                _view.SortDescriptions.Clear();
                // Group first so the three group headers always keep the Task Manager order.
                _view.SortDescriptions.Add(new SortDescription(nameof(ProcessRow.Group), ListSortDirection.Ascending));
                _view.SortDescriptions.Add(new SortDescription(path, ascending ? ListSortDirection.Ascending : ListSortDirection.Descending));
            }
        }
        catch { }

        ScheduleGroupStateRestore();
        UpdateFooter();
    }

    // ------------------------------------------------------------------ commands

    private List<ProcessRow> SelectedRows()
    {
        var rows = new List<ProcessRow>();
        try
        {
            foreach (var item in ProcessGrid.SelectedItems)
                if (item is ProcessRow row) rows.Add(row);
        }
        catch { }
        return rows;
    }

    private ProcessRow? SingleSelectedRow() =>
        ProcessGrid.SelectedItems.Count == 1 ? ProcessGrid.SelectedItem as ProcessRow : null;

    public override void OnPrimaryAction()
    {
        try
        {
            var rows = SelectedRows();
            if (rows.Count == 0) return;

            var protectedRows = rows.Where(IsProtected).ToList();
            if (protectedRows.Count > 0)
            {
                ShowMessage(
                    "This is a critical system process. Task Manager cannot end it:" +
                    Environment.NewLine + string.Join(Environment.NewLine, protectedRows.Select(r => $"{r.Name} (PID {r.Pid})")),
                    MessageBoxImage.Warning);
                rows = rows.Where(r => !IsProtected(r)).ToList();
                if (rows.Count == 0) return;
            }

            if (rows.Any(r => IsName(r, "explorer")))
            {
                var result = ShowQuestion(
                    "Ending explorer.exe restarts File Explorer and the Windows shell." +
                    Environment.NewLine + Environment.NewLine + "Do you want to end it?");
                if (result != MessageBoxResult.Yes) return;
            }

            var failed = new List<string>();
            foreach (var row in rows)
            {
                if (row.Pid <= 4) continue;
                try
                {
                    if (!ProcessActions.EndTaskGraceful(row.Pid)) failed.Add(row.Name);
                }
                catch { failed.Add(row.Name); }
            }

            if (failed.Count > 0)
                ShowMessage("Unable to end the following process(es). Access is denied:" +
                            Environment.NewLine + string.Join(Environment.NewLine, failed), MessageBoxImage.Warning);
        }
        catch { }
    }

    public override void ToggleEfficiencySelected()
    {
        try
        {
            var rows = SelectedRows();
            if (rows.Count == 0) return;

            var failed = new List<string>();
            foreach (var row in rows)
            {
                if (row.Pid <= 4) { failed.Add(row.Name); continue; }
                try
                {
                    bool enable = !row.EfficiencyMode;
                    if (ProcessActions.SetEfficiencyMode(row.Pid, enable))
                    {
                        row.EfficiencyMode = enable;
                        row.Status = enable ? "Efficiency mode" : "Running";
                    }
                    else failed.Add(row.Name);
                }
                catch { failed.Add(row.Name); }
            }

            PumpCellValues();

            if (failed.Count > 0)
                ShowMessage("Unable to change efficiency mode for:" +
                            Environment.NewLine + string.Join(Environment.NewLine, failed) +
                            Environment.NewLine + Environment.NewLine + "Access is denied.", MessageBoxImage.Warning);
        }
        catch { }
    }

    // ------------------------------------------------------------------ context menu

    private void OnGridPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        _mouseRightClickSeen = true;
        var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
        _rightClickOnRow = row is not null;
        if (row is null) return;

        if (!row.IsSelected)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == 0)
                ProcessGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
        ProcessGrid.CurrentItem = row.Item;
        try { row.Focus(); } catch { }
    }

    private void OnGridContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        bool keyboardInvocation = !_mouseRightClickSeen;
        _mouseRightClickSeen = false;

        if (!keyboardInvocation && !_rightClickOnRow)
        {
            e.Handled = true;
            return;
        }
        if (ProcessGrid.SelectedItems.Count == 0) e.Handled = true;
    }

    private void OnRowContextMenuOpened(object sender, RoutedEventArgs e)
    {
        int count = ProcessGrid.SelectedItems.Count;
        bool single = count == 1;
        var row = SingleSelectedRow();

        EndTaskMenu.IsEnabled = count > 0;
        EfficiencyMenu.IsEnabled = count > 0;
        EfficiencyMenu.IsChecked = row?.EfficiencyMode == true;
        RestartMenu.IsEnabled = single;
        DumpMenu.IsEnabled = single;
        OpenLocationMenu.IsEnabled = single && !string.IsNullOrWhiteSpace(row?.Path);
        SearchOnlineMenu.IsEnabled = single;
        PropertiesMenu.IsEnabled = single;
    }

    private void OnEndTaskMenu(object sender, RoutedEventArgs e) => OnPrimaryAction();
    private void OnEfficiencyMenu(object sender, RoutedEventArgs e) => ToggleEfficiencySelected();

    private void OnRestartMenu(object sender, RoutedEventArgs e)
    {
        var row = SingleSelectedRow();
        if (row is null) return;
        bool ok = false;
        try { ok = ProcessActions.Restart(row.Pid); } catch { }
        if (!ok) ShowMessage($"Unable to restart {row.Name}. Access is denied.", MessageBoxImage.Warning);
    }

    private async void OnCreateDumpMenu(object sender, RoutedEventArgs e)
    {
        var row = SingleSelectedRow();
        if (row is null) return;

        string path;
        try
        {
            string name = string.IsNullOrWhiteSpace(row.Name) ? "process" : row.Name;
            path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), name + ".DMP");
        }
        catch { return; }

        bool ok = false;
        try
        {
            Mouse.OverrideCursor = Cursors.Wait;
            ok = await Task.Run(() => ProcessActions.CreateDump(row.Pid, path));
        }
        catch { ok = false; }
        finally { Mouse.OverrideCursor = null; }

        if (ok)
            ShowMessage("A dump file has been created:" + Environment.NewLine + path, MessageBoxImage.Information);
        else
            ShowMessage($"Unable to create a dump file for {row.Name}. Access is denied.", MessageBoxImage.Warning);
    }

    private void OnOpenLocationMenu(object sender, RoutedEventArgs e)
    {
        var row = SingleSelectedRow();
        if (row is null || string.IsNullOrWhiteSpace(row.Path)) return;
        try
        {
            Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{row.Path}\"") { UseShellExecute = true });
        }
        catch { }
    }

    private void OnSearchOnlineMenu(object sender, RoutedEventArgs e)
    {
        var row = SingleSelectedRow();
        if (row is null) return;
        string term = string.IsNullOrWhiteSpace(row.Description) ? NormalizedName(row.Name) : row.Description;
        try
        {
            Process.Start(new ProcessStartInfo("https://www.bing.com/search?q=" + Uri.EscapeDataString(term))
            {
                UseShellExecute = true,
            });
        }
        catch { }
    }

    private void OnPropertiesMenu(object sender, RoutedEventArgs e) => OpenProperties(SingleSelectedRow());

    // ------------------------------------------------------------------ double click / properties

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            var row = FindAncestor<DataGridRow>(e.OriginalSource as DependencyObject);
            if (row?.Item is ProcessRow process) OpenProperties(process);
        }
        catch { }
    }

    private void OpenProperties(ProcessRow? row)
    {
        if (row is null) return;
        try
        {
            var dialog = new PropertiesWindow(row);
            var owner = Window.GetWindow(this);
            if (owner is not null) dialog.Owner = owner;
            dialog.ShowDialog();
        }
        catch { /* the process may already be gone */ }
    }

    // ------------------------------------------------------------------ helpers

    private static bool IsProtected(ProcessRow row) =>
        row.Pid <= 4 || CriticalNames.Contains(NormalizedName(row.Name));

    private static bool IsName(ProcessRow row, string name) =>
        string.Equals(NormalizedName(row.Name), name, StringComparison.OrdinalIgnoreCase);

    private static string NormalizedName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var value = name.Trim();
        if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
        return value;
    }

    private static T? FindAncestor<T>(DependencyObject? node) where T : DependencyObject
    {
        while (node is not null)
        {
            if (node is T match) return match;
            try { node = VisualTreeHelper.GetParent(node); }
            catch { return null; }
        }
        return null;
    }

    private void ShowMessage(string text, MessageBoxImage icon)
    {
        try
        {
            var owner = Window.GetWindow(this);
            if (owner is not null) MessageBox.Show(owner, text, "Task Manager", MessageBoxButton.OK, icon);
            else MessageBox.Show(text, "Task Manager", MessageBoxButton.OK, icon);
        }
        catch { }
    }

    private MessageBoxResult ShowQuestion(string text)
    {
        try
        {
            var owner = Window.GetWindow(this);
            return owner is not null
                ? MessageBox.Show(owner, text, "Task Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                : MessageBox.Show(text, "Task Manager", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        }
        catch { return MessageBoxResult.No; }
    }
}

/// <summary>Status text for the Processes grid (Task Manager shows "Efficiency mode" / "Suspended" / "Running").</summary>
public sealed class ProcessStatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ProcessRow row) return "";
        if (row.EfficiencyMode) return "Efficiency mode";
        if (!string.IsNullOrEmpty(row.Status)) return row.Status;
        if (row.Suspended) return "Suspended";
        return "Running";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>GPU column: percentage when the process uses a GPU engine, blank otherwise.</summary>
public sealed class ProcessGpuConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not ProcessRow row || !row.HasGpu) return "";
        return row.GpuPercent < 0.5 ? "0%" : $"{row.GpuPercent:0}%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Shows the "Efficiency mode" badge next to a process name.</summary>
public sealed class EfficiencyBadgeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is ProcessRow { EfficiencyMode: true } ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}