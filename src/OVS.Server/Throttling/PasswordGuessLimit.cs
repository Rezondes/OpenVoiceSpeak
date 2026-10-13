using System.Net;

namespace OVS.Server.Throttling;

/// <summary>
/// Package 91: wrong server passwords per source, the address group of IpAddresses.ToAddressGroup. Guarded by the
/// ServerState lock.
/// </summary>
internal sealed class PasswordGuessLimit
{
    readonly Dictionary<IPAddress, (int Count, DateTimeOffset Last)> failures = [];
    DateTimeOffset nextSweep;

    /// <summary>From the 5th wrong password on: blocked 1, 2, 4, ... minutes (at most 60) after the last wrong one.</summary>
    public static TimeSpan BlockFor(int count) =>
        count < ServerTuning.FreePasswordFailures ? TimeSpan.Zero
            : TimeSpan.FromMinutes(Math.Min(60, 1 << Math.Min(count - ServerTuning.FreePasswordFailures, 6)));

    /// <summary>True while the source is within its block after the last wrong password.</summary>
    public bool IsBlocked(IPAddress source, DateTimeOffset now) =>
        failures.TryGetValue(source, out var attempts) && now < attempts.Last + BlockFor(attempts.Count);

    /// <summary>A source is forgotten PasswordFailureMemory after its last wrong password; the forgotten ones are swept as often.</summary>
    public void RecordFailure(IPAddress source, DateTimeOffset now)
    {
        if (now >= nextSweep)
        {
            foreach (var (key, attempts) in failures)
                if (now - attempts.Last >= ServerTuning.PasswordFailureMemory) failures.Remove(key);
            nextSweep = now + ServerTuning.PasswordFailureMemory;
        }
        int count = failures.TryGetValue(source, out var old) && now - old.Last < ServerTuning.PasswordFailureMemory ? old.Count : 0;
        failures[source] = (count + 1, now);
    }

    internal int SourceCount => failures.Count;
}
