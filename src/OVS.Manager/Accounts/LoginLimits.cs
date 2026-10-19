using System.Net;

namespace OVS.Manager.Accounts;

/// <summary>
/// Package M3: more than <see cref="MaxFailures"/> failed logins from one address within <see cref="Window"/> are refused
/// before any password is checked; the lockout of an account (5 failures, 15 minutes) is Identity's own.
/// </summary>
internal sealed class LoginLimits(TimeProvider time)
{
    /// <summary>The failures an address may have within the window.</summary>
    public const int MaxFailures = 20;

    /// <summary>How long a failure counts.</summary>
    public static readonly TimeSpan Window = TimeSpan.FromMinutes(15);

    readonly Lock gate = new();
    readonly Dictionary<string, Queue<DateTimeOffset>> failures = [];

    /// <summary>True when <paramref name="address"/> used up its failures.</summary>
    public bool IsBlocked(IPAddress? address)
    {
        lock (gate) return Current(Key(address))?.Count >= MaxFailures;
    }

    /// <summary>One failed login from <paramref name="address"/>.</summary>
    public void RecordFailure(IPAddress? address)
    {
        lock (gate)
        {
            var key = Key(address);
            var queue = Current(key) ?? (failures[key] = new Queue<DateTimeOffset>());
            queue.Enqueue(time.GetUtcNow());
        }
    }

    /// <summary>The failures of <paramref name="key"/> still in the window, null for none; old ones are dropped.</summary>
    Queue<DateTimeOffset>? Current(string key)
    {
        if (!failures.TryGetValue(key, out var queue)) return null;
        var since = time.GetUtcNow() - Window;
        while (queue.Count > 0 && queue.Peek() <= since) queue.Dequeue();
        if (queue.Count > 0) return queue;
        failures.Remove(key); // ponytail: an address is forgotten once its window is empty; no sweep for addresses that never come back
        return null;
    }

    static string Key(IPAddress? address) => address?.ToString() ?? "";
}
