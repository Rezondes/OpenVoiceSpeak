namespace OVS.Server.Data;

/// <summary>
/// When the server data is written: at once after a change (<see cref="Persist"/>), debounced for logins and logouts
/// (<see cref="PersistSoon"/>, Package 87) with a retry while the file cannot be written, and once more on close.
/// Every member runs under <paramref name="gate"/>, the server state's lock; the timer takes the same lock.
/// </summary>
/// <param name="gate">The server state's lock.</param>
/// <param name="save">Writes the data file; throws when it cannot (a locked file, a full disk).</param>
/// <param name="time">The clock and the timer factory (a manual one in tests).</param>
/// <param name="log">A line for the server log.</param>
public sealed class SaveScheduler(object gate, Action save, TimeProvider time, Action<string> log)
{
    ITimer? timer;
    bool pending;
    bool failed; // the last write of the data file failed; logged once, and once more when a save succeeds again
    bool closed;
    readonly Dictionary<Guid, DateTimeOffset> savedAt = []; // Package 80: last save per key of PersistAtMostOncePerMinute

    /// <summary>Saves now; a failed save throws to the caller and leaves a pending debounced save pending.</summary>
    public void Persist()
    {
        save();
        pending = false; // a full save takes a pending debounced one along; a failed one leaves it pending
        if (failed)
        {
            failed = false;
            log("Serverdaten werden wieder gespeichert");
        }
    }

    /// <summary>Package 87: saves within SaveDelay, once for all changes until then; Close saves what is pending.</summary>
    public void PersistSoon()
    {
        if (pending) return;
        pending = true;
        timer ??= time.CreateTimer(_ =>
        {
            lock (gate)
            {
                if (!pending || closed) return;
                try
                {
                    Persist();
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException)
                {
                    // A virus scanner or backup tool holds the file, or the disk is full: keep the changes and try again.
                    if (!failed) log($"Serverdaten nicht gespeichert, neuer Versuch alle {Limits.SaveDelay.TotalSeconds:0} s: {e.Message}");
                    failed = true;
                    timer!.Change(Limits.SaveDelay, Timeout.InfiniteTimeSpan);
                }
            }
        }, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        timer.Change(Limits.SaveDelay, Timeout.InfiniteTimeSpan);
    }

    /// <summary>Writes a pending debounced save now.</summary>
    public void FlushPending()
    {
        if (pending) Persist();
    }

    /// <summary>Package 80: saves at most once a minute per key; later saves take the changes in between along.</summary>
    public void PersistAtMostOncePerMinute(Guid key)
    {
        var now = time.GetUtcNow();
        if (savedAt.TryGetValue(key, out var saved) && now - saved < TimeSpan.FromMinutes(1)) return;
        savedAt[key] = now;
        Persist();
    }

    /// <summary>
    /// The last save: stops the timer (a timer already waiting for the gate writes nothing either) and saves when
    /// <paramref name="force"/> is set or a save is pending; a failed save is logged, not thrown.
    /// </summary>
    public void Close(bool force)
    {
        lock (gate)
        {
            closed = true;
            timer?.Dispose(); // Package 87: nothing is written after this save (a restore replaces the file next)
            if (!force && !pending) return;
            try
            {
                Persist();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                log($"Serverdaten beim Beenden nicht gespeichert: {e.Message}");
            }
        }
    }
}
