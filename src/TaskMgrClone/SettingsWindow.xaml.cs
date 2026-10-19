using System.Windows;
using System.Windows.Controls;
using TaskMgrClone.Core;
using TaskMgrClone.UI;

namespace TaskMgrClone;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;

    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        _settings = settings;

        AlwaysOnTopCheck.IsChecked = settings.AlwaysOnTop;
        MinimizeOnUseCheck.IsChecked = settings.MinimizeOnUse;
        HideWhenMinimizedCheck.IsChecked = settings.HideWhenMinimized;
        FullAccountNameCheck.IsChecked = settings.ShowFullAccountName;

        foreach (var page in new[] { "Processes", "Performance", "App history", "Startup apps", "Users", "Details", "Services" })
            StartPageCombo.Items.Add(page);
        StartPageCombo.SelectedItem = page(settings.DefaultPage);

        SpeedCombo.Items.Add("High");
        SpeedCombo.Items.Add("Normal");
        SpeedCombo.Items.Add("Low");
        SpeedCombo.Items.Add("Paused");
        SpeedCombo.SelectedItem = !settings.RealTimeUpdates ? "Paused"
            : settings.RefreshIntervalMs <= 600 ? "High"
            : settings.RefreshIntervalMs >= 1800 ? "Low" : "Normal";

        ThemeCombo.Items.Add("Dark");
        ThemeCombo.Items.Add("Light");
        ThemeCombo.SelectedItem = settings.Theme.Equals("Light", StringComparison.OrdinalIgnoreCase) ? "Light" : "Dark";
    }

    private static string page(string name) =>
        new[] { "Processes", "Performance", "App history", "Startup apps", "Users", "Details", "Services" }
            .FirstOrDefault(p => p.Equals(name, StringComparison.OrdinalIgnoreCase)) ?? "Processes";

    private void OnOk(object sender, RoutedEventArgs e)
    {
        _settings.AlwaysOnTop = AlwaysOnTopCheck.IsChecked == true;
        _settings.MinimizeOnUse = MinimizeOnUseCheck.IsChecked == true;
        _settings.HideWhenMinimized = HideWhenMinimizedCheck.IsChecked == true;
        _settings.ShowFullAccountName = FullAccountNameCheck.IsChecked == true;

        var speed = SpeedCombo.SelectedItem as string ?? "Normal";
        _settings.RealTimeUpdates = speed != "Paused";
        _settings.RefreshIntervalMs = speed switch { "High" => 500, "Low" => 2000, _ => 1000 };

        _settings.Theme = ThemeCombo.SelectedItem as string ?? "Dark";
        _settings.DefaultPage = StartPageCombo.SelectedItem as string ?? "Processes";
        _settings.Save();
        DialogResult = true;
    }
}
