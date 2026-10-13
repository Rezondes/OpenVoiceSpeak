using System.Runtime.CompilerServices;

namespace OVS.Tests.TestSupport;

/// <summary>Waiting for network, voice and pool threads in the tests that run a real server.</summary>
static class AsyncWait
{
    /// <summary>
    /// Waits up to 10 s by the clock (not by a loop count: Task.Delay(20) may take far longer on a busy runner) and fails
    /// when the condition still does not hold, naming it.
    /// </summary>
    public static async Task UntilAsync(Func<bool> condition, [CallerArgumentExpression(nameof(condition))] string? expression = null) =>
        await UntilAsync(condition, TimeSpan.FromSeconds(10), expression);

    /// <summary>As <see cref="UntilAsync(Func{bool}, TimeSpan, string?)"/>, for a condition read through an await (the UI thread, a callback).</summary>
    public static async Task UntilAsync(Func<Task<bool>> condition, TimeSpan within, [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!await condition() && watch.Elapsed < within) await Task.Delay(20);
        Assert.True(await condition(), $"Not reached within {within.TotalSeconds:0} s: {expression}");
    }

    /// <summary>As <see cref="UntilAsync(Func{bool}, string?)"/>, for work known to take longer: waits up to <paramref name="within"/>.</summary>
    public static async Task UntilAsync(Func<bool> condition, TimeSpan within, [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.Elapsed < within) await Task.Delay(20);
        Assert.True(condition(), $"Not reached within {within.TotalSeconds:0} s: {expression}");
    }

    /// <summary>
    /// N23: a check that something does not happen on another thread: looks every 10 ms for <paramref name="duration"/>
    /// and fails at once, with <paramref name="reason"/>, when the condition turns true. Real time by nature: an absence
    /// can only be seen for a while.
    /// </summary>
    public static async Task AssertStaysFalseAsync(Func<bool> condition, TimeSpan duration, string reason,
        [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        do
        {
            Assert.False(condition(), $"{reason} ({expression} turned true after {watch.ElapsedMilliseconds} ms)");
            await Task.Delay(10);
        } while (watch.Elapsed < duration);
        Assert.False(condition(), $"{reason} ({expression} turned true at the end)");
    }
}
