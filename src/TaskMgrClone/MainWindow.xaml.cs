using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using TaskMgrClone.Core;
using TaskMgrClone.Pages;
using TaskMgrClone.UI;

namespace TaskMgrClone;

public partial class MainWindow : Window
{
    private sealed class NavItem : INotifyPropertyChanged
    {
        public required string Title { get; init; }
        public required string Icon { get; init; }
        public required Func<IDataPage> Create { get; init; }
        public IDataPage? Page { get; set; }

        private Visibility _labelVisibility = Visibility.Visible;
        public Visibility LabelVisibility
        {
            get => _labelVisibility;
            set
            {
                if (_labelVisibility == value) return;
                _labelVisibility = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LabelVisibility)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    private readonly List<NavItem> _nav;
    private readonly Sampler _sampler = new();
    private readonly AppHistoryStore _appHistory = App.History;
    private IDataPage? _currentPage;
    private bool _navCollapsed;
    private int _ticks;
    private System.Windows.Forms.NotifyIcon? _tray;
    private bool _exiting;

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        ApplyDarkTitleBar();
    }

    private void ApplyDarkTitleBar()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            int value = ThemeManager.IsDark ? 1 : 0;
            if (DwmSetWindowAttribute(handle, 20, ref value, sizeof(int)) != 0)
                DwmSetWindowAttribute(handle, 19, ref value, sizeof(int));
        }
        catch { }
    }

    public MainWindow()
    {
        InitializeComponent();

        _nav = new List<NavItem>
        {
            new() { Title = "Processes",    Icon = "\uE71D", Create = () => new ProcessesPage() },
            new() { Title = "Performance",  Icon = "\uE9D9", Create = () => new PerformancePage() },
            new() { Title = "App history",  Icon = "\uE81C", Create = () => new AppHistoryPage() },
            new() { Title = "Startup apps", Icon = "\uE945", Create = () => new StartupPage() },
            new() { Title = "Users",        Icon = "\uE77B", Create = () => new UsersPage() },
            new() { Title = "Details",      Icon = "\uE8A5", Create = () => new DetailsPage() },
            new() { Title = "Services",     Icon = "\uE713", Create = () => new ServicesPage() },
        };
        NavList.ItemsSource = _nav;

        ApplyWindowIcon();
        ApplySettings();

        _sampler.Sampled += () => Dispatcher.BeginInvoke(new Action(UpdateFromSample), DispatcherPriority.Background);
        _sampler.Start();

        Loaded += (_, _) =>
        {
            int index = _nav.FindIndex(n => n.Title.Equals(App.Settings.DefaultPage, StringComparison.OrdinalIgnoreCase));
            NavList.SelectedIndex = index >= 0 ? index : 0;
            UpdateSearchHint();
        };
    }

    // ------------------------------------------------------------------ shell

    private void ApplySettings()
    {
        Topmost = App.Settings.AlwaysOnTop;
        AlwaysOnTopMenu.IsChecked = App.Settings.AlwaysOnTop;
        _sampler.IntervalMs = App.Settings.RefreshIntervalMs;
        _sampler.Paused = !App.Settings.RealTimeUpdates;
    }

    private void ApplyWindowIcon()
    {
        try
        {
            var path = Path.Combine(Environment.SystemDirectory, "Taskmgr.exe");
            using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
            if (icon is not null)
                Icon = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
        }
        catch { /* cosmetic */ }
    }

    private void UpdateFromSample()
    {
        if (_sampler.Paused) return;
        _appHistory.Sample(_sampler.Processes);
        _currentPage?.OnSample(_sampler.System, _sampler.Processes);
        UpdateCommandBar();
        if (++_ticks % 30 == 0) _appHistory.Save();
    }

    private void UpdateCommandBar()
    {
        if (_currentPage is null) return;
        PageTitleText.Text = _currentPage.Title;
        PrimaryCommandText.Text = _currentPage.PrimaryButtonText;
        PrimaryCommandButton.Visibility = string.IsNullOrEmpty(_currentPage.PrimaryButtonText)
            ? Visibility.Collapsed : Visibility.Visible;
        EfficiencyButton.Visibility = _currentPage.SupportsEfficiencyMode ? Visibility.Visible : Visibility.Collapsed;
        SearchHost.Visibility = _currentPage.CanSearch ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnNavSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (NavList.SelectedItem is not NavItem item) return;
        item.Page ??= item.Create();
        _currentPage = item.Page;
        PageHost.Content = item.Page;
        item.Page.SetSearch(SearchBox.Text);
        item.Page.OnSample(_sampler.System, _sampler.Processes);
        UpdateCommandBar();
    }

    // ------------------------------------------------------------------ commands

    private void OnPrimaryCommand(object sender, RoutedEventArgs e)
    {
        if (_currentPage is null) return;
        _currentPage.OnPrimaryAction();
        UpdateCommandBar();
        if (App.Settings.MinimizeOnUse) WindowState = WindowState.Minimized;
    }

    private void OnEfficiencyCommand(object sender, RoutedEventArgs e) => _currentPage?.ToggleEfficiencySelected();

    private void OnRunNewTask(object sender, RoutedEventArgs e)
    {
        var dialog = new RunNewTaskWindow { Owner = this };
        if (dialog.ShowDialog() == true && dialog.Command.Length > 0)
            Win32Ext.RunTask(dialog.Command, dialog.Elevated);
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        UpdateSearchHint();
        _currentPage?.SetSearch(SearchBox.Text);
    }

    private void UpdateSearchHint() => SearchHint.Visibility = SearchBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Compact the command bar on narrow windows, mirroring Task Manager's responsive toolbar.
        bool compact = e.NewSize.Width < 1060;
        EfficiencyLabel.Visibility = compact ? Visibility.Collapsed : Visibility.Visible;
        SearchHost.Width = compact ? 140 : 170;
    }

    private void OnToggleNav(object sender, RoutedEventArgs e)
    {
        _navCollapsed = !_navCollapsed;
        NavColumn.Width = new GridLength(_navCollapsed ? 56 : 220);
        foreach (var item in _nav)
            item.LabelVisibility = _navCollapsed ? Visibility.Collapsed : Visibility.Visible;
    }

    private void OnOpenSettings(object sender, RoutedEventArgs e)
    {
        var dialog = new SettingsWindow(App.Settings) { Owner = this };
        if (dialog.ShowDialog() != true) return;
        ApplySettings();
        ThemeManager.Apply(App.Settings.Theme);
        ApplyDarkTitleBar();
        if (!App.Settings.HideWhenMinimized && _tray is { Visible: true })
        {
            _tray.Visible = false;
            ShowInTaskbar = true;
        }
    }

    private void OnOverflow(object sender, RoutedEventArgs e)
    {
        AlwaysOnTopMenu.IsChecked = App.Settings.AlwaysOnTop;
        if (OverflowButton.ContextMenu is { } menu)
        {
            menu.PlacementTarget = OverflowButton;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnToggleAlwaysOnTop(object sender, RoutedEventArgs e)
    {
        App.Settings.AlwaysOnTop = AlwaysOnTopMenu.IsChecked;
        Topmost = App.Settings.AlwaysOnTop;
    }

    private void OnRefreshNow(object sender, RoutedEventArgs e)
    {
        _currentPage?.RefreshNow();
        UpdateFromSample();
    }

    private void OnAbout(object sender, RoutedEventArgs e)
    {
        MessageBox.Show(this,
            "Task Manager\nVersion 1.0 (clean-room reconstruction of Windows Task Manager)\n\n" +
            "Processes, Performance, App history, Startup apps, Users, Details and Services.",
            "Task Manager", MessageBoxButton.OK, MessageBoxImage.Information);
    }

    // ------------------------------------------------------------------ tray / lifecycle

    private void EnsureTray()
    {
        if (_tray is not null) return;
        _tray = new System.Windows.Forms.NotifyIcon
        {
            Text = "Task Manager",
            Icon = System.Drawing.SystemIcons.Application,
        };
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("Open Task Manager", null, (_, _) => RestoreFromTray());
        menu.Items.Add("Exit", null, (_, _) => { _exiting = true; Close(); });
        _tray.ContextMenuStrip = menu;
        _tray.DoubleClick += (_, _) => RestoreFromTray();
    }

    private void HideToTray()
    {
        EnsureTray();
        if (_tray is null) return;
        _tray.Visible = true;
        ShowInTaskbar = false;
        Hide();
    }

    private void RestoreFromTray()
    {
        if (_tray is not null) _tray.Visible = false;
        ShowInTaskbar = true;
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState == WindowState.Minimized && App.Settings.HideWhenMinimized)
            HideToTray();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        _sampler.Dispose();
        _appHistory.Save();
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
            _tray = null;
        }
        if (_exiting) Application.Current.Shutdown();
    }

    // ------------------------------------------------------------------ keyboard

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        bool ctrl = (Keyboard.Modifiers & ModifierKeys.Control) != 0;
        bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) != 0;

        if (ctrl && shift && e.Key == Key.N)
        {
            OnRunNewTask(this, new RoutedEventArgs());
            e.Handled = true;
            return;
        }
        if (ctrl && (e.Key == Key.Tab || e.Key == Key.PageDown || e.Key == Key.PageUp))
        {
            Navigate(shift || e.Key == Key.PageUp ? -1 : 1);
            e.Handled = true;
            return;
        }
        if (ctrl && e.Key is >= Key.D1 and <= Key.D7)
        {
            NavList.SelectedIndex = (int)e.Key - (int)Key.D1;
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Delete:
                if (_currentPage is { SupportsEndTask: true }) _currentPage.OnPrimaryAction();
                e.Handled = true;
                break;
            case Key.F5:
                _currentPage?.RefreshNow();
                UpdateFromSample();
                e.Handled = true;
                break;
            case Key.F10:
                OnOpenSettings(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.F1:
                OnAbout(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    /// <summary>Used by --renderpages to walk every tab for deterministic screenshots.</summary>
    public void NavigateToPage(int index)
    {
        if (index >= 0 && index < _nav.Count) NavList.SelectedIndex = index;
    }

    private void Navigate(int direction)
    {
        if (_nav.Count == 0) return;
        int index = NavList.SelectedIndex + direction;
        if (index < 0) index = _nav.Count - 1;
        if (index >= _nav.Count) index = 0;
        NavList.SelectedIndex = index;
    }
}