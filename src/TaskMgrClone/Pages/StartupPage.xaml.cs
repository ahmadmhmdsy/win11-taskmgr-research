using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TaskMgrClone.Core;
using TaskMgrClone.UI;

namespace TaskMgrClone.Pages;

public partial class StartupPage : PageBase
{
    private readonly ObservableCollection<StartupPageRow> _rows = new();
    private readonly Dictionary<string, string> _pendingStatus = new(StringComparer.OrdinalIgnoreCase);
    private int _ticks;

    public StartupPage()
    {
        InitializeComponent();
        StartupGrid.ItemsSource = _rows;
        UpdateBiosText();
    }

    public override string Title => "Startup apps";
    public override bool CanSearch => false;

    public override string PrimaryButtonText
    {
        get
        {
            var selected = SelectedRows();
            if (selected.Count == 0) return "Enable";
            return selected.All(r => r.IsEnabled) ? "Disable" : "Enable";
        }
    }

    public override void OnSample(SystemSnapshot system, Dictionary<int, ProcessRow> processes)
    {
        try
        {
            if (_ticks++ % 3 != 0) return;
            RefreshRows();
            UpdateBiosText();
        }
        catch { /* a page must never throw while sampling */ }
    }

    public override void RefreshNow()
    {
        try
        {
            RefreshRows();
            UpdateBiosText();
        }
        catch { }
    }

    public override void OnPrimaryAction() => ToggleSelected();

    // ------------------------------------------------------------------ data

    private void RefreshRows()
    {
        var fresh = new List<StartupPageRow>();
        foreach (var source in StartupApps.Enumerate())
        {
            var row = new StartupPageRow();
            row.Load(source);
            if (_pendingStatus.TryGetValue(row.Identifier, out var pending))
            {
                if (string.Equals(source.Status, pending, StringComparison.OrdinalIgnoreCase)) _pendingStatus.Remove(row.Identifier);
                else row.Status = pending;
            }
            fresh.Add(row);
        }

        ListSync.SyncByKey(_rows, fresh, r => r.Identifier, (target, source) => target.CopyFrom(source));
        EmptyPanel.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateBiosText()
    {
        try
        {
            var value = StartupApps.LastBiosSeconds();
            BiosText.Text = string.IsNullOrEmpty(value) || value == "N/A"
                ? "Last BIOS time: N/A"
                : "Last BIOS time: " + value + " seconds";
        }
        catch { BiosText.Text = "Last BIOS time: N/A"; }
    }

    private List<StartupPageRow> SelectedRows() => StartupGrid.SelectedItems.OfType<StartupPageRow>().ToList();

    // ------------------------------------------------------------------ commands

    private void ToggleSelected()
    {
        var selected = SelectedRows();
        if (selected.Count == 0) return;
        SetEnabled(selected, !selected.All(r => r.IsEnabled));
    }

    private void SetEnabled(IEnumerable<StartupPageRow> rows, bool enable)
    {
        bool failed = false;
        bool changed = false;
        foreach (var row in rows)
        {
            var source = row.Source;
            if (source is null) continue;
            try
            {
                if (StartupApps.SetEnabled(source, enable))
                {
                    row.Status = enable ? "Enabled" : "Disabled";
                    _pendingStatus[row.Identifier] = row.Status;
                    changed = true;
                }
                else failed = true;
            }
            catch { failed = true; }
        }

        if (changed)
        {
            try { RefreshRows(); } catch { }
        }
        if (failed)
        {
            ShowMessage("Windows couldn't change this startup app.\n\nChanging startup apps that belong to all users usually requires administrator rights.",
                "Startup apps", MessageBoxImage.Warning);
        }
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (ItemsControl.ContainerFromElement(StartupGrid, e.OriginalSource as DependencyObject) is not DataGridRow row) return;
            if (row.Item is not StartupPageRow item) return;
            SetEnabled(new[] { item }, !item.IsEnabled);
            e.Handled = true;
        }
        catch { }
    }

    private void OnGridPreviewRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        try
        {
            if (ItemsControl.ContainerFromElement(StartupGrid, e.OriginalSource as DependencyObject) is not DataGridRow row) return;
            if (row.IsSelected) return;
            StartupGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }
        catch { }
    }

    private void OnStartupContextMenuOpened(object sender, RoutedEventArgs e)
    {
        try
        {
            if (sender is not ContextMenu menu || menu.Items.Count == 0 || menu.Items[0] is not MenuItem toggle) return;
            var selected = SelectedRows();
            toggle.Header = selected.Count > 0 && selected.All(r => r.IsEnabled) ? "Disable" : "Enable";
        }
        catch { }
    }

    private void OnContextToggle(object sender, RoutedEventArgs e) => ToggleSelected();

    private void OnContextOpenLocation(object sender, RoutedEventArgs e)
    {
        var row = SelectedRows().FirstOrDefault();
        if (row is null) return;
        OpenFileLocation(row);
    }

    private void OnContextSearchOnline(object sender, RoutedEventArgs e)
    {
        var row = SelectedRows().FirstOrDefault();
        if (row is null) return;
        try
        {
            var query = Uri.EscapeDataString(row.Name + " startup app");
            Process.Start(new ProcessStartInfo("https://www.bing.com/search?q=" + query) { UseShellExecute = true });
        }
        catch { }
    }

    private void OpenFileLocation(StartupPageRow row)
    {
        try
        {
            var path = !string.IsNullOrWhiteSpace(row.ExecutablePath)
                ? row.ExecutablePath
                : StartupApps.FirstToken(row.Command);
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"") { UseShellExecute = true });
                return;
            }
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Win32Ext.ShellOpen(path);
                return;
            }
            ShowMessage("Windows couldn't find the file for this startup app.\n\n" +
                       (string.IsNullOrEmpty(path) ? "No file path was recorded." : path),
                "Startup apps", MessageBoxImage.Information);
        }
        catch { }
    }

    private void ShowMessage(string message, string title, MessageBoxImage icon)
    {
        var owner = Window.GetWindow(this);
        if (owner is null) MessageBox.Show(message, title, MessageBoxButton.OK, icon);
        else MessageBox.Show(owner, message, title, MessageBoxButton.OK, icon);
    }
}

/// <summary>Row view model for the Startup apps table (notifies in place so status changes show immediately).</summary>
public sealed class StartupPageRow : Observable
{
    private string _name = "";
    private string _publisher = "";
    private string _status = "Enabled";
    private string _impact = "Not measured";
    private string _command = "";
    private string _executablePath = "";

    public StartupRow? Source { get; private set; }
    public string Identifier { get; private set; } = "";
    public string SourceName { get; private set; } = "";

    public string Name { get => _name; set => Set(ref _name, value); }
    public string Publisher { get => _publisher; set => Set(ref _publisher, value); }
    public string Status { get => _status; set => Set(ref _status, value); }
    public string Impact { get => _impact; set => Set(ref _impact, value); }
    public string Command { get => _command; set => Set(ref _command, value); }
    public string ExecutablePath { get => _executablePath; set => Set(ref _executablePath, value); }

    public bool IsEnabled => string.Equals(_status, "Enabled", StringComparison.OrdinalIgnoreCase);

    public void Load(StartupRow source)
    {
        Source = source;
        Identifier = source.Identifier;
        SourceName = source.Source;
        Name = source.Name;
        Publisher = source.Publisher;
        Status = string.IsNullOrEmpty(source.Status) ? "Disabled" : source.Status;
        Impact = string.IsNullOrWhiteSpace(source.Impact) ? "Not measured" : source.Impact;
        Command = source.Command;
        ExecutablePath = source.ExecutablePath;
    }

    public void CopyFrom(StartupPageRow other)
    {
        Source = other.Source;
        Identifier = other.Identifier;
        SourceName = other.SourceName;
        Name = other.Name;
        Publisher = other.Publisher;
        Status = other.Status;
        Impact = other.Impact;
        Command = other.Command;
        ExecutablePath = other.ExecutablePath;
    }
}
