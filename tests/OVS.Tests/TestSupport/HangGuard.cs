namespace OVS.Tests.TestSupport;

/// <summary>
/// A test-side guard only, nothing in src depends on it: a task that never completes (an answer that never comes on a
/// manual clock, a cancel that does nothing) fails the test with a <see cref="TimeoutException"/> after <see cref="Limit"/>
/// instead of hanging the run. A passing test never waits for it.
/// </summary>
static class HangGuard
{
    public static readonly TimeSpan Limit = TimeSpan.FromSeconds(10);

    public static Task BoundAsync(Task task) => task.WaitAsync(Limit);

    public static Task<T> BoundAsync<T>(Task<T> task) => task.WaitAsync(Limit);
}
