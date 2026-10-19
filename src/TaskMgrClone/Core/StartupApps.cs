using System.Diagnostics;
using Microsoft.Win32;

namespace TaskMgrClone.Core;

internal static class StartupApps
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Run32Key = @"Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
    private const string ApprovedRun32Key = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run32";
    private const string ApprovedFolderKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder";

    public static List<StartupRow> Enumerate()
    {
        var rows = new List<StartupRow>();
        AddRegistry(rows, Registry.CurrentUser, RunKey, @"HKCU\" + RunKey, ApprovedKey, "Current user");
        AddRegistry(rows, Registry.LocalMachine, RunKey, @"HKLM\" + RunKey, ApprovedKey, "All users");
        AddRegistry(rows, Registry.LocalMachine, Run32Key, @"HKLM\" + Run32Key, ApprovedRun32Key, "All users (32-bit)");
        AddFolder(rows, Environment.GetFolderPath(Environment.SpecialFolder.Startup), "Current user");
        AddFolder(rows, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), "All users");
        return rows.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void AddRegistry(List<StartupRow> rows, RegistryKey hive, string path, string display, string approvedPath, string source)
    {
        using var key = hive.OpenSubKey(path);
        if (key == null) return;
        using var approvedMachine = hive.OpenSubKey(approvedPath);
        using var approvedUser = Registry.CurrentUser.OpenSubKey(approvedPath);
        foreach (var name in key.GetValueNames())
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            var command = key.GetValue(name) as string ?? "";
            var exe = FirstToken(command);
            var row = new StartupRow
            {
                Name = name,
                Command = command,
                ExecutablePath = exe,
                RegistryKey = display,
                Identifier = name,
                Source = source,
                Publisher = PublisherOf(exe),
                Status = IsEnabledBlob((approvedMachine?.GetValue(name) as byte[]) ?? (approvedUser?.GetValue(name) as byte[])) ? "Enabled" : "Disabled",
            };
            rows.Add(row);
        }
    }

    private static void AddFolder(List<StartupRow> rows, string folder, string source)
    {
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
        using var approved = Registry.CurrentUser.OpenSubKey(ApprovedFolderKey);
        foreach (var file in Directory.EnumerateFiles(folder))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
            rows.Add(new StartupRow
            {
                Name = name,
                Command = file,
                ExecutablePath = file,
                RegistryKey = folder,
                Identifier = name,
                Source = source,
                IsFolder = true,
                Publisher = PublisherOf(file),
                Status = IsEnabled(approved, name) ? "Enabled" : "Disabled",
            });
        }
    }

    /// <summary>StartupApproved blobs are 12+ bytes; even first byte = enabled.</summary>
    private static bool IsEnabled(RegistryKey? approved, string name) => IsEnabledBlob(approved?.GetValue(name) as byte[]);

    /// <summary>StartupApproved blob: even first byte means the entry is enabled.</summary>
    internal static bool IsEnabledBlob(byte[]? bytes)
    {
        if (bytes is { Length: > 0 }) return (bytes[0] & 1) == 0;
        return true;
    }

    public static bool SetEnabled(StartupRow row, bool enabled)
    {
        var approvedPath = row.IsFolder
            ? (row.Source == "Current user" || row.Source == "All users" ? ApprovedFolderKey : ApprovedFolderKey)
            : row.RegistryKey.Contains("WOW6432Node") ? ApprovedRun32Key : ApprovedKey;
        var hive = row.RegistryKey.StartsWith("HKCU", StringComparison.OrdinalIgnoreCase) || row.IsFolder
            ? Registry.CurrentUser
            : Registry.CurrentUser; // per-user override wins for all sources
        try
        {
            using var key = hive.CreateSubKey(approvedPath);
            var blob = new byte[12];
            blob[0] = (byte)(enabled ? 0x02 : 0x03);
            key.SetValue(row.Identifier, blob, RegistryValueKind.Binary);
            row.Status = enabled ? "Enabled" : "Disabled";
            return true;
        }
        catch { return false; }
    }

    internal static string FirstToken(string command)
    {
        if (string.IsNullOrWhiteSpace(command)) return "";
        command = command.Trim();
        if (command.StartsWith('"'))
        {
            var end = command.IndexOf('"', 1);
            return end > 0 ? command[1..end] : command;
        }
        var space = command.IndexOf(' ');
        return space > 0 ? command[..space] : command;
    }

    private static string PublisherOf(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                return info.CompanyName ?? "";
            }
        }
        catch { }
        return "";
    }

    public static string LastBiosSeconds()
    {
        // Boot performance is published by the kernel boot provider; best-effort read from the
        // persisted diagnostic value, otherwise unknown.
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\Session Manager\Power");
            if (key?.GetValue("LastBiosTime") is int seconds) return seconds.ToString();
        }
        catch { }
        return "N/A";
    }
}