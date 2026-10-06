using System.Net;

namespace OVS.Server.Throttling;

/// <summary>
/// Package 86: at most AdminTokenFailures wrong admin tokens per source (the address group of IpAddresses.ToAddressGroup)
/// within AdminTokenWindow of the first, even across sessions. Guarded by the ServerState lock.
/// </summary>
public sealed class AdminTokenGuessLimit
{
    readonly Dictionary<IPAddress, (int Count, DateTimeOffset First)> failures = [];

    /// <summary>An ended window is forgotten here.</summary>
    public bool IsBlocked(IPAddress source, DateTimeOffset now)
    {
        if (failures.TryGetValue(source, out var f) && now - f.First >= Limits.AdminTokenWindow)
            failures.Remove(source);
        return failures.GetValueOrDefault(source).Count >= Limits.AdminTokenFailures;
    }

    /// <summary>Counts a wrong admin token; the window runs from the first one.</summary>
    public void RecordFailure(IPAddress source, DateTimeOffset now)
    {
        var (count, first) = failures.GetValueOrDefault(source, (0, now));
        failures[source] = (count + 1, first);
    }
}
