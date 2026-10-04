namespace OVS.Client.ViewModels;

/// <summary>
/// Package 139 (A148): the attempts after a lost connection, 1, 2, 4, 8 and 16 s apart (Fluxer's 1 s base within its
/// 30 s window). Each series has a generation number; an attempt that ends after <see cref="Stop"/> or a new series is
/// outdated and its result is ignored.
/// </summary>
/// <param name="post">Runs the attempts on the UI thread, like everything else that touches the connection.</param>
public sealed class Reconnector(TimeProvider time, Action<Action> post)
{
    public static readonly IReadOnlyList<TimeSpan> Delays = [.. new[] { 1, 2, 4, 8, 16 }.Select(s => TimeSpan.FromSeconds(s))];

    int generation;
    int attempts;
    ITimer? timer;
    Func<int, Task<bool>>? attempt;
    Action? gaveUp;

    public bool IsRunning => attempt is not null;

    /// <summary>When the next attempt starts, for the countdown; null while one runs or no series does.</summary>
    public DateTimeOffset? NextAttemptAt { get; private set; }

    /// <summary>The attempt of this generation still counts: its connection may be used.</summary>
    public bool IsCurrent(int attemptGeneration) => IsRunning && attemptGeneration == generation;

    /// <param name="attempt">Gets the generation and tells whether it connected.</param>
    /// <param name="gaveUp">After the last attempt failed.</param>
    public void Start(Func<int, Task<bool>> attempt, Action gaveUp)
    {
        Stop();
        this.attempt = attempt;
        this.gaveUp = gaveUp;
        attempts = 0;
        Schedule();
    }

    public void Stop()
    {
        generation++;
        timer?.Dispose();
        timer = null;
        attempt = null;
        gaveUp = null;
        NextAttemptAt = null;
    }

    void Schedule()
    {
        var delay = Delays[attempts];
        int series = generation;
        NextAttemptAt = time.GetUtcNow() + delay;
        timer = time.CreateTimer(_ => post(() => _ = RunAsync(series)), null, delay, Timeout.InfiniteTimeSpan);
    }

    async Task RunAsync(int series)
    {
        if (series != generation || attempt is not { } run) return;
        timer?.Dispose();
        timer = null;
        NextAttemptAt = null;
        attempts++;
        bool connected = await run(series);
        if (series != generation) return; // stopped or started anew meanwhile
        if (connected) Stop();
        else if (attempts < Delays.Count) Schedule();
        else
        {
            var done = gaveUp;
            Stop();
            done?.Invoke();
        }
    }
}
