namespace OVS.Server;

/// <summary>
/// Package 192 (A204): the different users admitted since local midnight (the day follows <c>TZ</c>, like the automatic
/// restart) and how many of them were seen for the first time today. The fingerprints stay in memory and never leave
/// the server; only the two counts are exported. A restart starts the day's count again.
/// </summary>
internal sealed class DailyUsers(TimeProvider time)
{
    readonly object gate = new(); // the gauges read from the exporter's thread
    readonly HashSet<string> today = [];
    readonly HashSet<string> newToday = [];
    DateOnly day = Today(time);

    /// <summary>The different users of today.</summary>
    public int Count
    {
        get { lock (gate) return Current().Count; }
    }

    /// <summary>The users of today whose first visit was today.</summary>
    public int NewCount
    {
        get
        {
            lock (gate)
            {
                Current();
                return newToday.Count;
            }
        }
    }

    /// <summary>One admitted session of the user <paramref name="fingerprint"/>, who was first seen at <paramref name="firstSeen"/>.</summary>
    public void Add(string fingerprint, DateTimeOffset firstSeen)
    {
        lock (gate)
        {
            Current().Add(fingerprint);
            if (DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(firstSeen, time.LocalTimeZone).DateTime) == day) newToday.Add(fingerprint);
        }
    }

    /// <summary>Today's set, emptied first when the local day changed since the last call.</summary>
    HashSet<string> Current()
    {
        var now = Today(time);
        if (now == day) return today;
        day = now;
        today.Clear();
        newToday.Clear();
        return today;
    }

    static DateOnly Today(TimeProvider time) => DateOnly.FromDateTime(time.GetLocalNow().DateTime);
}
