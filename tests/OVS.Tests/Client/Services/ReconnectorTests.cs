using OVS.Client.Services;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>Package 139 (A148): the attempts after a lost connection, on a manual clock.</summary>
public sealed class ReconnectorTests
{
    readonly ManualTimeProvider time = new();
    readonly Reconnector reconnector;

    public ReconnectorTests() => reconnector = new Reconnector(time, action => action());

    double Elapsed(DateTimeOffset start) => (time.GetUtcNow() - start).TotalSeconds;

    void Run(int seconds)
    {
        for (int index = 0; index < seconds; index++) time.Advance(TimeSpan.FromSeconds(1));
    }

    [Fact]
    public void Schedule_1_2_4_8_16_ThenGivesUp()
    {
        var start = time.GetUtcNow();
        var at = new List<double>();
        bool gaveUp = false;
        reconnector.Start(_ =>
        {
            at.Add(Elapsed(start));
            return Task.FromResult(false);
        }, () => gaveUp = true);
        Assert.Equal(start + TimeSpan.FromSeconds(1), reconnector.NextAttemptAt); // the countdown's target

        Run(30);
        Assert.Equal([1, 3, 7, 15], at);
        Assert.False(gaveUp);
        Run(1);
        Assert.Equal([1, 3, 7, 15, 31], at);
        Assert.True(gaveUp); // after the fifth failure
        Assert.False(reconnector.IsRunning);
        Run(60);
        Assert.Equal(5, at.Count);

        // the first success ends a series
        start = time.GetUtcNow();
        at.Clear();
        gaveUp = false;
        reconnector.Start(_ =>
        {
            at.Add(Elapsed(start));
            return Task.FromResult(at.Count == 2);
        }, () => gaveUp = true);
        Run(60);
        Assert.Equal([1, 3], at);
        Assert.False(gaveUp);
        Assert.False(reconnector.IsRunning);
        Assert.Null(reconnector.NextAttemptAt);
    }

    [Fact]
    public void OutdatedAttempt_Ignored()
    {
        var attempts = new List<(int Generation, TaskCompletionSource<bool> Result)>();
        int gaveUp = 0;
        Task<bool> Attempt(int generation)
        {
            var result = new TaskCompletionSource<bool>();
            attempts.Add((generation, result));
            return result.Task;
        }

        reconnector.Start(Attempt, () => gaveUp++);
        Run(1);
        var first = attempts.Single();
        Assert.True(reconnector.IsCurrent(first.Generation));
        Assert.Null(reconnector.NextAttemptAt); // running, no countdown

        reconnector.Stop(); // "Abbrechen"
        Assert.False(reconnector.IsCurrent(first.Generation));
        first.Result.SetResult(false); // its late failure plans nothing
        Run(60);
        Assert.Single(attempts);
        Assert.Equal(0, gaveUp);
        Assert.False(reconnector.IsRunning);

        // a new series: an attempt of the old one that ends late changes nothing in it
        reconnector.Start(Attempt, () => gaveUp++);
        Run(1);
        var old = attempts[1];
        reconnector.Start(Attempt, () => gaveUp++); // e.g. lost again meanwhile
        Assert.False(reconnector.IsCurrent(old.Generation));
        old.Result.SetResult(true); // the old success does not end the new series
        Assert.True(reconnector.IsRunning);
        Run(1);
        Assert.Equal(3, attempts.Count);
        Assert.True(reconnector.IsCurrent(attempts[2].Generation));
        attempts[2].Result.SetResult(true);
        Assert.False(reconnector.IsRunning);
        Assert.Equal(0, gaveUp);
    }
}
