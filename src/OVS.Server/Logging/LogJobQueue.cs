namespace OVS.Server.Logging;

/// <summary>Package 81: one session's log pages and searches, one after the other on the thread pool, never under the state lock.</summary>
public sealed class LogJobQueue
{
    readonly object gate = new();
    Task jobs = Task.CompletedTask;
    int queued;

    /// <summary>Runs job after the session's earlier ones; false when Limits.MaxQueuedLogJobs are waiting already.</summary>
    public bool TryQueue(Action job)
    {
        if (Interlocked.Increment(ref queued) > Limits.MaxQueuedLogJobs)
        {
            Interlocked.Decrement(ref queued);
            return false;
        }
        lock (gate)
            jobs = jobs.ContinueWith(_ =>
            {
                try
                {
                    job();
                }
                finally
                {
                    Interlocked.Decrement(ref queued);
                }
            }, TaskScheduler.Default);
        return true;
    }
}
