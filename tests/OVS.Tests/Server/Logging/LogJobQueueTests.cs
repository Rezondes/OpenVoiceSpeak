using OVS.Server;
using OVS.Server.Logging;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Logging;

public sealed class LogJobQueueTests : IDisposable
{
    static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);
    readonly LogJobQueue queue = new();
    readonly List<ManualResetEventSlim> events = [];

    public void Dispose()
    {
        foreach (var manualResetEventSlim in events) manualResetEventSlim.Set(); // lets every blocked job end
        foreach (var manualResetEventSlim in events) manualResetEventSlim.Dispose();
    }

    ManualResetEventSlim NewEvent()
    {
        var manualResetEventSlim = new ManualResetEventSlim();
        events.Add(manualResetEventSlim);
        return manualResetEventSlim;
    }

    /// <summary>Queues a job that signals once it runs and then waits for its release.</summary>
    (ManualResetEventSlim Running, ManualResetEventSlim Release) QueueBlockingJob()
    {
        var (running, release) = (NewEvent(), NewEvent());
        Assert.True(queue.TryQueue(() =>
        {
            running.Set();
            release.Wait(Timeout);
        }));
        return (running, release);
    }

    /// <summary>Behind exactly one queued job, queues jobs until the budget is used; returns the signal of the last one.</summary>
    ManualResetEventSlim FillBehindOneJob()
    {
        var lastRan = NewEvent();
        for (int index = 1; index < ServerTuning.MaxQueuedLogJobs - 1; index++) Assert.True(queue.TryQueue(() => { }));
        Assert.True(queue.TryQueue(lastRan.Set));
        return lastRan;
    }

    [Fact]
    public void TryQueue_SeveralJobs_RunOneAfterTheOtherInOrder()
    {
        var order = new List<int>();
        var lastRan = NewEvent();
        var first = QueueBlockingJob();
        Assert.True(queue.TryQueue(() => order.Add(1)));
        Assert.True(queue.TryQueue(() => order.Add(2)));
        Assert.True(queue.TryQueue(() =>
        {
            order.Add(3);
            lastRan.Set();
        }));
        Assert.True(first.Running.Wait(Timeout));
        Assert.Empty(order);
        first.Release.Set();
        Assert.True(lastRan.Wait(Timeout));
        Assert.Equal([1, 2, 3], order);
    }

    [Fact]
    public void TryQueue_MaxQueuedLogJobsWaiting_Refused()
    {
        QueueBlockingJob();
        FillBehindOneJob();
        Assert.False(queue.TryQueue(() => { }));
    }

    [Fact]
    public async Task TryQueue_AfterTheJobsRan_TakesAgain()
    {
        var first = QueueBlockingJob();
        FillBehindOneJob();
        first.Release.Set();
        var ran = NewEvent();
        bool queued = false;
        await AsyncWait.Until(() => queued || (queued = queue.TryQueue(ran.Set)));
        Assert.True(ran.Wait(Timeout));
    }

    /// <summary>A job runs only after the earlier ones ended, so while the next blocking job runs the queue holds just that one.</summary>
    [Fact]
    public void TryQueue_AfterARefusal_TheFullBudgetIsFreeAgain()
    {
        var first = QueueBlockingJob();
        var lastRan = FillBehindOneJob();
        Assert.False(queue.TryQueue(() => { }));
        first.Release.Set();
        Assert.True(lastRan.Wait(Timeout));
        var next = QueueBlockingJob();
        Assert.True(next.Running.Wait(Timeout));
        FillBehindOneJob();
        Assert.False(queue.TryQueue(() => { }));
    }

    [Fact]
    public void TryQueue_AfterAThrowingJob_NextJobRuns()
    {
        var first = QueueBlockingJob();
        Assert.True(queue.TryQueue(() => throw new InvalidOperationException("job failed")));
        var next = QueueBlockingJob();
        first.Release.Set();
        Assert.True(next.Running.Wait(Timeout));
    }

    [Fact]
    public void TryQueue_AfterAThrowingJob_ItsSlotIsFree()
    {
        var first = QueueBlockingJob();
        Assert.True(queue.TryQueue(() => throw new InvalidOperationException("job failed")));
        var next = QueueBlockingJob();
        first.Release.Set();
        Assert.True(next.Running.Wait(Timeout));
        FillBehindOneJob();
        Assert.False(queue.TryQueue(() => { }));
    }
}
