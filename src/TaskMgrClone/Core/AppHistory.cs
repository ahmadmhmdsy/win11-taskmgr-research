using System.IO;
using System.Text.Json;

namespace TaskMgrClone.Core;

/// <summary>
/// Task Manager's App history is backed by SRUM (an ESE database). We cannot read that without an
/// ESE engine, so this accumulates the same fields from live sampling and persists them locally.
/// </summary>
public sealed class AppHistoryStore
{
    private sealed class Entry
    {
        public double CpuSeconds { get; set; }
        public double BackgroundCpuSeconds { get; set; }
        public ulong NetworkBytes { get; set; }
        public ulong MeteredBytes { get; set; }
        public ulong TileUpdates { get; set; }
        public string Publisher { get; set; } = "";
    }

    private readonly Dictionary<string, Entry> _entries = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<int, double> _lastCpu = new();
    private readonly string _path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TaskMgrClone", "apphistory.json");

    public AppHistoryStore() => Load();

    public void Sample(Dictionary<int, ProcessRow> processes)
    {
        foreach (var row in processes.Values)
        {
            if (!row.IsPackaged && !row.HasWindow) continue;
            var name = DisplayName(row);
            if (string.IsNullOrEmpty(name)) continue;
            if (!_entries.TryGetValue(name, out var entry))
            {
                entry = new Entry { Publisher = row.Publisher };
                _entries[name] = entry;
            }
            if (_lastCpu.TryGetValue(row.Pid, out var previous) && row.CpuTimeSeconds >= previous)
            {
                var delta = row.CpuTimeSeconds - previous;
                entry.CpuSeconds += delta;
                if (!row.HasWindow) entry.BackgroundCpuSeconds += delta;
            }
            _lastCpu[row.Pid] = row.CpuTimeSeconds;
        }
        foreach (var dead in _lastCpu.Keys.Where(p => !processes.ContainsKey(p)).ToList()) _lastCpu.Remove(dead);
    }

    private static string DisplayName(ProcessRow row)
    {
        if (row.IsPackaged && !string.IsNullOrEmpty(row.Description)) return row.Description;
        return Path.GetFileNameWithoutExtension(row.Name);
    }

    public List<AppHistoryRow> Rows(Dictionary<int, ProcessRow> processes)
    {
        var live = processes.Values.Where(p => p.IsPackaged || p.HasWindow).GroupBy(DisplayName).ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase);
        var rows = new List<AppHistoryRow>();
        foreach (var (name, entry) in _entries)
        {
            if (entry.CpuSeconds < 0.1 && !live.ContainsKey(name)) continue;
            var current = live.TryGetValue(name, out var proc) ? proc : null;
            rows.Add(new AppHistoryRow
            {
                Name = name,
                Publisher = entry.Publisher,
                CpuTime = TimeSpan.FromSeconds(entry.CpuSeconds),
                CpuPercentNow = current?.CpuPercent ?? 0,
                NetworkBytes = entry.NetworkBytes,
                MeteredBytes = entry.MeteredBytes,
                TileUpdates = entry.TileUpdates,
                BackgroundCpuBytes = (ulong)(entry.BackgroundCpuSeconds * 1_000_000),
                BackgroundCpuPercent = entry.CpuSeconds > 0 ? 100.0 * entry.BackgroundCpuSeconds / entry.CpuSeconds : 0,
                BackgroundNetworkBytes = 0,
                BackgroundMeteredBytes = 0,
                Status = current == null ? "Suspended" : "Running",
            });
        }
        return rows.OrderByDescending(r => r.CpuTime).ToList();
    }

    public void DeleteAll()
    {
        _entries.Clear();
        Save();
    }

    public void Delete(AppHistoryRow row)
    {
        _entries.Remove(row.Name);
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var data = JsonSerializer.Deserialize<Dictionary<string, Entry>>(File.ReadAllText(_path));
            if (data == null) return;
            foreach (var (key, value) in data) _entries[key] = value;
        }
        catch { }
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            File.WriteAllText(_path, JsonSerializer.Serialize(_entries));
        }
        catch { }
    }
}
