using System.Collections.ObjectModel;

namespace TaskMgrClone.UI;

/// <summary>Reconciles an ObservableCollection with a sampling snapshot without rebuilding it
/// (keeps selection, scroll position and per-row animations stable).</summary>
public static class ListSync
{
    public static void Sync<T>(ObservableCollection<T> target, IEnumerable<T> desired) where T : class
    {
        var source = desired as IList<T> ?? desired.ToList();
        var desiredSet = new HashSet<T>(source, ReferenceEqualityComparer.Instance);
        for (int i = target.Count - 1; i >= 0; i--)
            if (!desiredSet.Contains(target[i])) target.RemoveAt(i);

        var currentSet = new HashSet<T>(target, ReferenceEqualityComparer.Instance);
        for (int i = 0; i < source.Count; i++)
        {
            var item = source[i];
            if (currentSet.Contains(item)) continue;
            if (i >= target.Count) target.Add(item);
            else target.Insert(i, item);
            currentSet.Add(item);
        }
    }

    /// <summary>Merge freshly-created rows (services/startup/users) by key, updating in place.</summary>
    public static void SyncByKey<T>(ObservableCollection<T> target, IList<T> fresh, Func<T, string> key, Action<T, T> update) where T : class
    {
        var map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in target) map[key(row)] = row;

        for (int i = 0; i < fresh.Count; i++)
        {
            var row = fresh[i];
            if (map.TryGetValue(key(row), out var existing))
            {
                update(existing, row);
            }
            else
            {
                if (i >= target.Count) target.Add(row);
                else target.Insert(i, row);
                map[key(row)] = row;
            }
        }

        var desired = new HashSet<string>(fresh.Select(key), StringComparer.OrdinalIgnoreCase);
        for (int i = target.Count - 1; i >= 0; i--)
            if (!desired.Contains(key(target[i]))) target.RemoveAt(i);
    }
}
