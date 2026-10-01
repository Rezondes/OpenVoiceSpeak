using System.Collections.ObjectModel;
using System.Runtime.CompilerServices;

namespace OVS.Client.ViewModels;

/// <summary>
/// Package 102: how long a removed entry stays in its list (the view sets the animated display's length; zero removes
/// at once, which is what view models without a view and the simplified display get).
/// </summary>
/// <param name="post">Runs the removal on the UI thread; inline when null (tests with a manual clock).</param>
public sealed class LeaveTimer(TimeProvider time, Func<Action<Action>?>? post = null)
{
    public TimeSpan Delay { get; set; }

    internal void After(Action action) => _ = Task.Delay(Delay, time).ContinueWith(_ =>
    {
        if (post?.Invoke() is { } onUi) onUi(action);
        else action();
    }, TaskScheduler.Default);
}

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

    static object? KeyOf(object? item) => item is IMotionKey keyed ? keyed.MotionKey : item;

    static Dictionary<object, int> Leaving(object list) => leavingSets.GetValue(list, _ => new Dictionary<object, int>(ReferenceEqualityComparer.Instance));

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
        var keys = desired.Select(KeyOf).ToHashSet();
        var final = desired.ToList();
        bool fold = leave is { Delay.Ticks: > 0 };
        if (fold)
        {
            int at = 0;
            foreach (var item in target)
            {
                var key = KeyOf(item);
                if (keys.Contains(key)) at = final.FindIndex(f => Equals(KeyOf(f), key)) + 1;
                else final.Insert(at++, item);
            }
        }

        for (int i = 0; i < final.Count; i++)
        {
            var want = final[i];
            int index = target.IndexOf(want);
            if (index < 0)
            {
                var key = KeyOf(want);
                int same = -1;
                for (int j = i; j < target.Count && same < 0; j++)
                    if (Equals(KeyOf(target[j]), key)) same = j;
                if (same < 0)
                {
                    target.Insert(i, want);
                    continue;
                }
                if (same != i) target.Move(same, i);
                target[i] = want; // a rebuilt row: the new object in the old one's place
            }
            else if (index != i) target.Move(index, i);
        }
        while (target.Count > final.Count) target.RemoveAt(target.Count - 1);

        var leaving = Leaving(target);
        var shown = new HashSet<object>(target, ReferenceEqualityComparer.Instance);
        foreach (var gone in leaving.Keys.Where(item => !shown.Contains(item)).ToList()) leaving.Remove(gone); // replaced by a rebuilt row
        foreach (var item in target.ToList()) // the instances really shown (a record may equal a new one)
        {
            if (keys.Contains(KeyOf(item)))
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
