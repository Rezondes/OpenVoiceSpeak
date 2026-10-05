using OVS.Server.Data.Records;

namespace OVS.Server.State;

public sealed partial class ServerState
{
    /// <summary>
    /// Saves after every change. Package 80 (A97): lifted and expired bans stay as history and are dropped on the way
    /// once they ended longer ago than the log retention (LogDays, 0 = forever).
    /// </summary>
    void Persist()
    {
        var now = time.GetUtcNow();
        if (data.Settings.LogDays > 0)
        {
            var cutoff = now.AddDays(-data.Settings.LogDays);
            data.Bans.RemoveAll(b => b.EndedAt(now) < cutoff);
        }
        if (now >= nextPrune) PruneGuests(now);
        BeforeSave?.Invoke();
        store.Save(data);
        savePending = false; // a full save takes a pending debounced one along; a failed one leaves it pending
        if (saveFailed)
        {
            saveFailed = false;
            logs.Server("Serverdaten werden wieder gespeichert");
        }
    }

    /// <summary>Tests only: runs before every write of the data file; throwing simulates a locked file or a full disk.</summary>
    public Action? BeforeSave { get; set; }

    /// <summary>Package 87: writes a pending debounced save now (tests and tools that read the data file).</summary>
    public void FlushPendingSave()
    {
        lock (gate)
            if (savePending) Persist();
    }

    /// <summary>Package 87: saves within SaveDelay, once for all changes until then; CloseAll saves what is pending.</summary>
    void PersistSoon()
    {
        if (savePending) return;
        savePending = true;
        saveTimer ??= time.CreateTimer(_ =>
        {
            lock (gate)
            {
                if (!savePending || closedWith is not null) return;
                try
                {
                    Persist();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A virus scanner or backup tool holds the file, or the disk is full: keep the changes and try again.
                    if (!saveFailed) logs.Server($"Serverdaten nicht gespeichert, neuer Versuch alle {Limits.SaveDelay.TotalSeconds:0} s: {e.Message}");
                    saveFailed = true;
                    saveTimer!.Change(Limits.SaveDelay, Timeout.InfiniteTimeSpan);
                }
            }
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        saveTimer.Change(Limits.SaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Package 87: drops unused guest records, checked at start and then once a day; true when some went.</summary>
    bool PruneGuests(DateTimeOffset now)
    {
        nextPrune = now + TimeSpan.FromDays(1);
        int removed = data.PruneGuests(now - Limits.PruneGuestsAfter);
        if (removed > 0)
            logs.Server($"{removed} ungenutzte Gast-Einträge entfernt (nur Gast, kein Bann, über {Limits.PruneGuestsAfter.TotalDays:0} Tage nicht angemeldet)");
        return removed > 0;
    }

    /// <summary>Package 80 (A97): counts a join the ban turned away; written at most once per minute per ban, later saves take the rest along.</summary>
    void RecordBlockedAttempt(BanRecord ban, string ip, DateTimeOffset now)
    {
        ban.BlockedAttempts++;
        ban.LastAttempt = now;
        ban.LastAttemptIp = ip;
        if (attemptSaved.TryGetValue(ban.Id, out var saved) && now - saved < TimeSpan.FromMinutes(1)) return;
        attemptSaved[ban.Id] = now;
        Persist();
    }
}
