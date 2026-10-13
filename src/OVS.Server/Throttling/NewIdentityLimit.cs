using System.Net;

namespace OVS.Server.Throttling;

/// <summary>
/// Package 87: unknown fingerprints admitted per source (the address group of IpAddresses.ToAddressGroup) within
/// NewIdentityWindow of the first; known ones are never counted. Guarded by the ServerState lock.
/// </summary>
internal sealed class NewIdentityLimit
{
    /// <summary>Per source: its window, the identities counted in it and whether a refusal was reported.</summary>
    readonly Dictionary<IPAddress, (int Count, DateTimeOffset First, bool RefusalReported)> windows = [];
    DateTimeOffset nextSweep;

    /// <summary>Counts a new identity of this source; false above the limit, <paramref name="firstRefusal"/> once per window.</summary>
    public bool TryCount(IPAddress source, DateTimeOffset now, out bool firstRefusal)
    {
        if (now >= nextSweep)
        {
            foreach (var (key, window) in windows)
                if (now - window.First >= ServerTuning.NewIdentityWindow) windows.Remove(key);
            nextSweep = now + ServerTuning.NewIdentityWindow;
        }
        (int Count, DateTimeOffset First, bool RefusalReported) entry = windows.TryGetValue(source, out var old) && now - old.First < ServerTuning.NewIdentityWindow ? old : (0, now, false);
        if (entry.Count >= ServerTuning.NewIdentitiesPerHour)
        {
            firstRefusal = !entry.RefusalReported;
            windows[source] = entry with { RefusalReported = true };
            return false;
        }
        firstRefusal = false;
        windows[source] = entry with { Count = entry.Count + 1 };
        return true;
    }

    internal int SourceCount => windows.Count;
}
