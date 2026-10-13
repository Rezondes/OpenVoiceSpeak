using Xunit.Sdk;

namespace OVS.Tests.TestSupport;

/// <summary>N23: the waits of the tests that run real threads.</summary>
public sealed class AsyncWaitTests
{
    [Fact]
    public async Task AssertStaysFalse_StaysFalse_Passes()
    {
        await AsyncWait.AssertStaysFalseAsync(() => false, TimeSpan.FromMilliseconds(30), "nothing happens");
    }

    [Fact]
    public async Task AssertStaysFalse_TurnsTrue_FailsBeforeTheEndWithTheReason()
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        bool happened = false;
        _ = Task.Run(async () =>
        {
            await Task.Delay(20);
            Volatile.Write(ref happened, true);
        });

        var error = await Assert.ThrowsAsync<FalseException>(() =>
            AsyncWait.AssertStaysFalseAsync(() => Volatile.Read(ref happened), TimeSpan.FromSeconds(10), "it must not happen"));

        Assert.Contains("it must not happen", error.Message, StringComparison.Ordinal);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "it fails when the condition turns true, not at the end");
    }

    [Fact]
    public async Task AssertStaysFalse_TrueAtOnce_FailsAtOnce()
    {
        await Assert.ThrowsAsync<FalseException>(() => AsyncWait.AssertStaysFalseAsync(() => true, TimeSpan.FromSeconds(10), "already there"));
    }
}
