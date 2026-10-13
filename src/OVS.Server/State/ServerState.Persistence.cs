using OVS.Server.Logging;

namespace OVS.Server.State;

public sealed partial class ServerState
{
    /// <summary>
    /// The save action of <see cref="saves"/>, run under the lock: drops the bans past the log retention (Package 80, A97)
    /// and, once a day, the unused guests, then writes the data file.
    /// </summary>
    void WriteData()
    {
        var now = time.GetUtcNow();
        data.PruneEndedBans(now);
        PruneGuests(now);
        BeforeSave?.Invoke();
        store.Save(data);
    }

    /// <summary>Tests only: runs before every write of the data file; throwing simulates a locked file or a full disk.</summary>
    public Action? BeforeSave { get; set; }

    /// <summary>Package 87: writes a pending debounced save now (tests and tools that read the data file).</summary>
    public void FlushPendingSave()
    {
        lock (gate) saves.FlushPending();
    }

    /// <summary>Package 87: drops unused guest records, checked at start and then once a day; true when some went.</summary>
    bool PruneGuests(DateTimeOffset now)
    {
        if (now < nextPrune) return false;
        nextPrune = now + TimeSpan.FromDays(1);
        int removed = data.PruneGuests(now - ServerTuning.PruneGuestsAfter);
        if (removed > 0)
            logs.Server.GuestsPruned(removed, ServerTuning.PruneGuestsAfter.TotalDays);
        return removed > 0;
    }
}
