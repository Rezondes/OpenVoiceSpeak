namespace OVS.Client.Services;

/// <summary>
/// Package 102: how long a removed entry stays in its list (the view sets the animated display's length; zero removes
/// at once, which is what view models without a view and the simplified display get).
/// </summary>
/// <param name="post">Runs the removal on the UI thread; inline when null (tests with a manual clock).</param>
/// <param name="time">The clock the delay runs on.</param>
public sealed class LeaveTimer(TimeProvider time, Func<Action<Action>?>? post = null)
{
    /// <summary>How long a removed entry stays.</summary>
    public TimeSpan Delay { get; set; }

    internal void After(Action action) => _ = Task.Delay(Delay, time).ContinueWith(_ =>
    {
        if (post?.Invoke() is { } onUi) onUi(action);
        else action();
    }, TaskScheduler.Default);
}
