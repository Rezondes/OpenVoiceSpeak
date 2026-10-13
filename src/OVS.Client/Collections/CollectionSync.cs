using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;
using OVS.Client.Services;

namespace OVS.Client.Collections;

/// <summary>
/// Package 102: keeps a shown list equal to what it should show. A removed entry may stay a moment as leaving, so the
/// view can fold it away; leaving belongs to the entry in that one list (a user who switches channels leaves the old
/// channel's list while entering the new one).
/// </summary>
public static class CollectionSync
{
    /// <summary>Per list: the entries leaving, each with the round it started in (a newer leave outlives an older timer).</summary>
    static readonly ConditionalWeakTable<object, Dictionary<object, int>> leavingSets = [];
    static int rounds;

    /// <summary>An entry of a list starts (true) or stops (false, it came back) leaving: (list, entry, leaving).</summary>
    public static event Action<object, object, bool>? LeavingChanged;

    static Dictionary<object, int> Leaving(object list) => leavingSets.GetValue(list, _ => new Dictionary<object, int>(ReferenceEqualityComparer.Instance));

    /// <summary>True while item is folding away from list.</summary>
    public static bool IsLeaving(object list, object item) => leavingSets.TryGetValue(list, out var set) && set.ContainsKey(item);

    /// <summary>The entries that really are there: commands, counts and targets never see leaving ones.</summary>
    public static IEnumerable<T> Live<T>(this ObservableCollection<T> list) where T : class
    {
        if (!leavingSets.TryGetValue(list, out var set) || set.Count == 0) return list;
        return list.Where(item => !set.ContainsKey(item));
    }

    /// <summary>
    /// Moves, inserts and removes so the collection shows <paramref name="desired"/>, keeping existing instances (and
    /// selection). An entry with the same key as one shown (a rebuilt administration row) takes its place. With a leave
    /// delay a removed entry stays leaving behind the entry it followed until the delay is over; one that comes back
    /// meanwhile is the same entry again.
    /// </summary>
    public static void Sync<T>(ObservableCollection<T> target, IReadOnlyList<T> desired, LeaveTimer? leave = null) where T : class
    {
        var keys = desired.Select(IMotionKey.KeyOf).ToHashSet();
        var final = desired.ToList();
        if (leave is { Delay.Ticks: > 0 })
        {
            // each leaving entry stays behind the entry it followed (one that is still wanted, or the start)
            var head = new List<T>();
            var behind = new Dictionary<object, List<T>>();
            object? after = null;
            foreach (var item in target)
            {
                var key = IMotionKey.KeyOf(item);
                if (keys.Contains(key)) after = key;
                else if (after is null) head.Add(item);
                else if (behind.TryGetValue(after, out var group)) group.Add(item);
                else behind[after] = [item];
            }
            final = [.. head];
            foreach (var want in desired)
            {
                final.Add(want);
                if (IMotionKey.KeyOf(want) is { } key && behind.TryGetValue(key, out var group)) final.AddRange(group);
            }
        }
        else
        {
            // Package 122: gone at once, so they do not move down step by step to the end first
            for (int index = target.Count - 1; index >= 0; index--)
                if (!keys.Contains(IMotionKey.KeyOf(target[index]))) target.RemoveAt(index);
        }

        // Package 122: what is shown, so no entry is searched for in the whole list (quadratic with long lists)
        var present = new HashSet<T>(target);
        var presentKeys = target.Select(IMotionKey.KeyOf).ToHashSet();
        for (int index = 0; index < final.Count; index++)
        {
            var want = final[index];
            if (index < target.Count && Equals(target[index], want)) continue;
            var key = IMotionKey.KeyOf(want);
            if (!present.Contains(want) && !presentKeys.Contains(key))
            {
                target.Insert(index, want);
                present.Add(want);
                presentKeys.Add(key);
                continue;
            }
            // shown further down, or a rebuilt row with the same key
            int at = -1;
            for (int otherIndex = index; otherIndex < target.Count && at < 0; otherIndex++)
                if (Equals(target[otherIndex], want) || Equals(IMotionKey.KeyOf(target[otherIndex]), key)) at = otherIndex;
            if (at < 0)
            {
                target.Insert(index, want); // only a key equal to an entry placed already: as before, a new entry
                present.Add(want);
                continue;
            }
            if (at != index) target.Move(at, index);
            if (!Equals(target[index], want))
            {
                present.Remove(target[index]);
                target[index] = want; // a rebuilt row: the new object in the old one's place
                present.Add(want);
            }
        }
        while (target.Count > final.Count) target.RemoveAt(target.Count - 1);

        var leaving = Leaving(target);
        var shown = new HashSet<object>(target, ReferenceEqualityComparer.Instance);
        foreach (var gone in leaving.Keys.Where(item => !shown.Contains(item)).ToList()) leaving.Remove(gone); // replaced by a rebuilt row
        foreach (var item in target.ToList()) // the instances really shown (a record may equal a new one)
        {
            if (keys.Contains(IMotionKey.KeyOf(item)))
            {
                if (leaving.Remove(item)) LeavingChanged?.Invoke(target, item, false); // back while leaving
                continue;
            }
            if (leaving.ContainsKey(item)) continue;
            int round = leaving[item] = ++rounds;
            LeavingChanged?.Invoke(target, item, true);
            leave!.After(() =>
            {
                if (!leaving.TryGetValue(item, out var mine) || mine != round) return; // came back, maybe left again since
                leaving.Remove(item);
                target.Remove(item);
            });
        }
    }
}
