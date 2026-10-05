using System.Runtime.CompilerServices;

namespace OVS.Tests.TestSupport;

/// <summary>Waiting for network, voice and pool threads in the tests that run a real server.</summary>
static class AsyncWait
{
    /// <summary>
    /// Waits up to 10 s by the clock (not by a loop count: Task.Delay(20) may take far longer on a busy runner) and fails
    /// when the condition still does not hold, naming it.
    /// </summary>
    public static async Task Until(Func<bool> condition, [CallerArgumentExpression(nameof(condition))] string? expression = null)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition() && watch.ElapsedMilliseconds < 10_000) await Task.Delay(20);
        Assert.True(condition(), $"Not reached within 10 s: {expression}");
    }
}
