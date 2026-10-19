using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using TaskMgrClone.Controls;
using TaskMgrClone.Core;
using TaskMgrClone.UI;

namespace TaskMgrClone.Pages;

public partial class UsersPage : PageBase
{
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ProcessIdToSessionId(uint processId, out uint sessionId);

    private readonly ObservableCollection<UserPageCard> _rows = new();
    private Dictionary<int, ProcessRow> _processes = new();

    public UsersPage()
    {
        InitializeComponent();
        UsersList.ItemsSource = _rows;
    }

    public override string Title => "Users";
    public override bool CanSearch => false;
    public override string PrimaryButtonText => "Sign out";

    public override void OnSample(SystemSnapshot system, Dictionary<int, ProcessRow> processes)
    {
        _processes = processes ?? new Dictionary<int, ProcessRow>();
        try { Rebuild(); } catch { /* a page must never throw while sampling */ }
    }

    public override void RefreshNow()
    {
        try { Rebuild(); } catch { }
    }

    public override void OnPrimaryAction()
    {
        if (UsersList.SelectedItem is not UserPageCard card) return;
        SignOut(card);
    }

    // ------------------------------------------------------------------ data

    private void Rebuild()
    {
        List<UserRow> sessions;
        try { sessions = Sessions.Enumerate(_processes); }
        catch { sessions = new List<UserRow>(); }

        var aggregates = AggregateBySession(_processes);
        var fresh = new List<UserPageCard>(sessions.Count);
        foreach (var session in sessions)
        {
            var card = new UserPageCard();
            card.Load(session, aggregates.TryGetValue(session.SessionId, out var aggregate) ? aggregate : SessionAggregate.Empty);
            fresh.Add(card);
        }

        ListSync.SyncByKey(_rows, fresh, r => r.SessionId.ToString(), (target, source) => target.CopyFrom(source));
        EmptyPanel.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Aggregates per session. Sessions.Enumerate does this from ProcessRow.SessionId, but the sampler
    /// leaves that field at 0, so map PIDs to sessions here when it is missing.
    /// </summary>
    private static Dictionary<uint, SessionAggregate> AggregateBySession(Dictionary<int, ProcessRow> processes)
    {
        var map = new Dictionary<uint, SessionAggregate>();
        if (processes == null) return map;
        foreach (var process in processes.Values)
        {
            if (!TrySessionId(process, out var sessionId)) continue;
            if (!map.TryGetValue(sessionId, out var aggregate))
            {
                aggregate = new SessionAggregate();
                map[sessionId] = aggregate;
            }
            aggregate.ProcessCount++;
            aggregate.CpuPercent += process.CpuPercent;
            aggregate.MemoryBytes += process.WorkingSetBytes;
            aggregate.DiskBytesPerSec += process.DiskBytesPerSec;
            aggregate.NetworkBytesPerSec += process.NetworkBytesPerSec ?? 0;
        }
        return map;
    }

    private static bool TrySessionId(ProcessRow process, out uint sessionId)
    {
        if (process.SessionId > 0)
        {
            sessionId = (uint)process.SessionId;
            return true;
        }
        try { return ProcessIdToSessionId((uint)process.Pid, out sessionId); }
        catch { sessionId = 0; return false; }
    }

    // ------------------------------------------------------------------ commands

    private void OnDisconnect(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } card) return;
        try
        {
            if (Win32Ext.DisconnectSession(card.SessionId)) return;
        }
        catch { }
        ShowFailure("disconnect", card, "This may require administrator rights, or the session may no longer be active.");
    }

    private void OnSignOutClick(object sender, RoutedEventArgs e)
    {
        if (CardOf(sender) is not { } card) return;
        SignOut(card);
    }

    private void SignOut(UserPageCard card)
    {
        if (!Confirm("Sign out " + card.UserName + "?\n\nApps that belong to this user will be closed and unsaved work may be lost.",
                "Sign out")) return;
        try
        {
            if (Win32Ext.LogoffSession(card.SessionId)) return;
        }
        catch { }
        ShowFailure("sign out", card, "This may require administrator rights, or the session may no longer be active.");
    }

    private static UserPageCard? CardOf(object sender) => (sender as FrameworkElement)?.DataContext as UserPageCard;

    private void ShowFailure(string action, UserPageCard card, string detail)
    {
        var message = "Windows couldn't " + action + " " + card.UserName + ".\n\n" + detail;
        var owner = Window.GetWindow(this);
        if (owner is null) MessageBox.Show(message, "Users", MessageBoxButton.OK, MessageBoxImage.Warning);
        else MessageBox.Show(owner, message, "Users", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private bool Confirm(string message, string title)
    {
        var owner = Window.GetWindow(this);
        return owner is null
            ? MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes
            : MessageBox.Show(owner, message, title, MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes;
    }
}

/// <summary>Per-session totals used by the Users page cards.</summary>
public sealed class SessionAggregate
{
    public static readonly SessionAggregate Empty = new();
    public int ProcessCount;
    public double CpuPercent;
    public ulong MemoryBytes;
    public double DiskBytesPerSec;
    public double NetworkBytesPerSec;
}

/// <summary>Card view model for one signed-in session (notifies in place so the card refreshes live).</summary>
public sealed class UserPageCard : Observable
{
    private string _userName = "";
    private string _status = "";
    private string _statusLine = "";
    private string _processLine = "";
    private int _processCount;
    private double _cpuPercent;
    private ulong _memoryBytes;
    private double _diskBytesPerSec;
    private double _networkBytesPerSec;
    private string _cpuText = "0%";
    private string _memoryText = "";
    private string _diskText = "";
    private string _networkText = "";

    public uint SessionId { get; private set; }
    public string UserName { get => _userName; private set => Set(ref _userName, value); }
    public string Status { get => _status; private set => Set(ref _status, value); }
    public string StatusLine { get => _statusLine; private set => Set(ref _statusLine, value); }
    public string ProcessLine { get => _processLine; private set => Set(ref _processLine, value); }
    public int ProcessCount { get => _processCount; private set => Set(ref _processCount, value); }
    public double CpuPercent { get => _cpuPercent; private set => Set(ref _cpuPercent, value); }
    public ulong MemoryBytes { get => _memoryBytes; private set => Set(ref _memoryBytes, value); }
    public double DiskBytesPerSec { get => _diskBytesPerSec; private set => Set(ref _diskBytesPerSec, value); }
    public double NetworkBytesPerSec { get => _networkBytesPerSec; private set => Set(ref _networkBytesPerSec, value); }

    public string CpuText { get => _cpuText; private set => Set(ref _cpuText, value); }
    public string MemoryText { get => _memoryText; private set => Set(ref _memoryText, value); }
    public string DiskText { get => _diskText; private set => Set(ref _diskText, value); }
    public string NetworkText { get => _networkText; private set => Set(ref _networkText, value); }

    public void Load(UserRow session, SessionAggregate aggregate)
    {
        SessionId = session.SessionId;
        var name = string.IsNullOrWhiteSpace(session.UserName) ? "Session " + session.SessionId : session.UserName;
        var parts = new List<string>();
        if (!string.IsNullOrWhiteSpace(session.Status)) parts.Add(session.Status);
        if (!string.IsNullOrWhiteSpace(session.SessionName)) parts.Add(session.SessionName);
        if (!string.IsNullOrWhiteSpace(session.ClientName)) parts.Add(session.ClientName);

        UserName = name;
        Status = session.Status ?? "";
        StatusLine = parts.Count == 0 ? "Session " + session.SessionId : string.Join("  \u00B7  ", parts);
        ProcessCount = aggregate.ProcessCount;
        ProcessLine = aggregate.ProcessCount == 1 ? "1 process" : aggregate.ProcessCount + " processes";
        CpuPercent = aggregate.CpuPercent;
        MemoryBytes = aggregate.MemoryBytes;
        DiskBytesPerSec = aggregate.DiskBytesPerSec;
        NetworkBytesPerSec = aggregate.NetworkBytesPerSec;
        CpuText = Format.Percent(aggregate.CpuPercent);
        MemoryText = Format.Bytes(aggregate.MemoryBytes);
        DiskText = Format.Rate(aggregate.DiskBytesPerSec);
        NetworkText = Format.Mbps(aggregate.NetworkBytesPerSec);
    }

    public void CopyFrom(UserPageCard other)
    {
        SessionId = other.SessionId;
        UserName = other.UserName;
        Status = other.Status;
        StatusLine = other.StatusLine;
        ProcessLine = other.ProcessLine;
        ProcessCount = other.ProcessCount;
        CpuPercent = other.CpuPercent;
        MemoryBytes = other.MemoryBytes;
        DiskBytesPerSec = other.DiskBytesPerSec;
        NetworkBytesPerSec = other.NetworkBytesPerSec;
        CpuText = other.CpuText;
        MemoryText = other.MemoryText;
        DiskText = other.DiskText;
        NetworkText = other.NetworkText;
    }
}
