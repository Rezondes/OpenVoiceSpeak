using OVS.Server.Logging;
using OVS.Server.State;

namespace OVS.Server;

/// <summary>The daily automatic restart: when the next one is due and the wait for it.</summary>
internal static class RestartSchedule
{
    /// <summary>
    /// Package 69: waits for the daily restart as set in the administration and follows every change of it at once.
    /// </summary>
    /// <returns>True when the restart time came, false after a stop request.</returns>
    public static async Task<bool> WaitAsync(ServerState state, TimeProvider time, ServerLogWriter logs, CancellationToken stop)
    {
        var sync = new object();
        CancellationTokenSource? current = null;
        void OnChanged()
        {
            lock (sync) current?.Cancel(); // only disposes the timer; the loop below continues on the thread pool
        }

        state.SettingsChanged += OnChanged;
        try
        {
            (bool On, TimeOnly At)? logged = null;
            while (true)
            {
                using var changed = CancellationTokenSource.CreateLinkedTokenSource(stop);
                lock (sync) current = changed; // before reading the settings, so no change slips through
                var (on, at) = state.AutoRestart;
                var wait = Timeout.InfiniteTimeSpan;
                if (on)
                {
                    var next = FindNext(time.GetUtcNow(), at, time.LocalTimeZone);
                    if (logged != (on, at))
                        #pragma warning disable CA1873 // the log files and the console are always on, so the argument is always used
                        logs.Server.AutoRestartScheduled(at, TimeZoneInfo.ConvertTime(next, time.LocalTimeZone));
                        #pragma warning restore CA1873
                    wait = next - time.GetUtcNow();
                }
                else if (logged is { On: true })
                {
                    logs.Server.AutoRestartOff();
                }
                logged = (on, at);

                var delay = Task.Delay(wait, time, changed.Token);
                await delay.ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing | ConfigureAwaitOptions.ForceYielding);
                lock (sync) current = null;
                if (stop.IsCancellationRequested) return false;
                if (delay.IsCompletedSuccessfully) return true;
            }
        }
        finally
        {
            state.SettingsChanged -= OnChanged;
        }
    }

    /// <summary>
    /// The next moment the wall clock in the zone shows the given time. One second of slack, so a timer that fires
    /// a hair early cannot make the next run restart right away a second time.
    /// </summary>
    public static DateTimeOffset FindNext(DateTimeOffset now, TimeOnly at, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(now.AddSeconds(1), zone).DateTime;
        var candidate = local.Date + at.ToTimeSpan();
        if (candidate <= local) candidate = candidate.AddDays(1);
        // ponytail: a time inside a DST gap does not exist; GetUtcOffset then uses the standard offset, at most an hour off twice a year
        return new DateTimeOffset(candidate, zone.GetUtcOffset(candidate));
    }
}
