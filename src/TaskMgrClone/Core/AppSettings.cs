using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaskMgrClone.Core;

public sealed class AppSettings : Observable
{
    private static readonly string Dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskMgrClone");
    private static readonly string FilePath = Path.Combine(Dir, "settings.json");

    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true, DefaultIgnoreCondition = JsonIgnoreCondition.Never };

    private int _refreshIntervalMs = 1000;
    private bool _alwaysOnTop;
    private bool _minimizeOnUse;
    private bool _hideWhenMinimized;
    private bool _showFullAccountName;
    private bool _realTimeUpdates = true;
    private bool _hideWhenMinimizedEnabled = true;
    private string _defaultPage = "Processes";
    private string _theme = "Dark";
    private bool _showHiddenAppHistory;
    private bool _efficiencyModeDefault;

    [JsonIgnore] public int RefreshIntervalMsOnDisk { get; set; }

    public int RefreshIntervalMs { get => _refreshIntervalMs; set { if (Set(ref _refreshIntervalMs, value)) Save(); } }
    public bool AlwaysOnTop { get => _alwaysOnTop; set { if (Set(ref _alwaysOnTop, value)) Save(); } }
    public bool MinimizeOnUse { get => _minimizeOnUse; set { if (Set(ref _minimizeOnUse, value)) Save(); } }
    public bool HideWhenMinimized { get => _hideWhenMinimized; set { if (Set(ref _hideWhenMinimized, value)) Save(); } }
    public bool ShowFullAccountName { get => _showFullAccountName; set { if (Set(ref _showFullAccountName, value)) Save(); } }
    public bool RealTimeUpdates { get => _realTimeUpdates; set { if (Set(ref _realTimeUpdates, value)) Save(); } }
    public bool HideWhenMinimizedEnabled { get => _hideWhenMinimizedEnabled; set { if (Set(ref _hideWhenMinimizedEnabled, value)) Save(); } }
    public string DefaultPage { get => _defaultPage; set { if (Set(ref _defaultPage, value)) Save(); } }
    public string Theme { get => _theme; set { if (Set(ref _theme, value)) Save(); } }
    public bool ShowHiddenAppHistory { get => _showHiddenAppHistory; set { if (Set(ref _showHiddenAppHistory, value)) Save(); } }
    public bool EfficiencyModeDefault { get => _efficiencyModeDefault; set { if (Set(ref _efficiencyModeDefault, value)) Save(); } }

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var json = File.ReadAllText(FilePath);
                var loaded = JsonSerializer.Deserialize<AppSettings>(json, Options);
                if (loaded != null)
                {
                    loaded._saveEnabled = false;
                    return loaded;
                }
            }
        }
        catch { }
        var settings = new AppSettings { _saveEnabled = false };
        return settings;
    }

    private bool _saveEnabled = true;

    public void Save()
    {
        if (!_saveEnabled) { _saveEnabled = true; return; }
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Options));
        }
        catch { }
    }
}
