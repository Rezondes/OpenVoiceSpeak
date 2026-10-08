using Avalonia.Headless.XUnit;
using OVS.Client.Animations;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>
/// Package 109: the counters every animation started from code passes. On the UI thread, where every animation starts
/// and ends, so no other test's animation counts in between.
/// </summary>
[Collection(MotionCollection.Name)]
public sealed class AnimationCounterTests
{
    [AvaloniaFact]
    public void Count_UnfinishedTask_ReturnsItAndRaisesStartedAndRunning()
    {
        var run = new TaskCompletionSource();
        int started = AnimationCounter.Started, running = AnimationCounter.Running;
        var counted = AnimationCounter.Count(run.Task);
        try
        {
            Assert.Same(run.Task, counted);
            Assert.Equal((started + 1, running + 1), (AnimationCounter.Started, AnimationCounter.Running));
        }
        finally
        {
            run.SetResult();
        }
    }

    [AvaloniaTheory]
    [InlineData(TaskStatus.RanToCompletion)]
    [InlineData(TaskStatus.Canceled)]
    [InlineData(TaskStatus.Faulted)]
    public void Count_TaskEnded_NoLongerRunningStillStarted(TaskStatus ending)
    {
        var run = new TaskCompletionSource();
        int started = AnimationCounter.Started, running = AnimationCounter.Running;
        AnimationCounter.Count(run.Task);
        switch (ending)
        {
            case TaskStatus.RanToCompletion:
                run.SetResult();
                break;
            case TaskStatus.Canceled:
                run.SetCanceled();
                break;
            default:
                run.SetException(new InvalidOperationException("the animation failed"));
                _ = run.Task.Exception; // observed: no unobserved task exception later
                break;
        }
        Assert.Equal(ending, run.Task.Status);
        Assert.Equal((started + 1, running), (AnimationCounter.Started, AnimationCounter.Running));
    }

    [AvaloniaFact]
    public void ResetStarted_OneRunning_StartedZeroRunningKept()
    {
        var run = new TaskCompletionSource();
        AnimationCounter.Count(run.Task);
        int running = AnimationCounter.Running;
        AnimationCounter.ResetStarted();
        try
        {
            Assert.Equal((0, running), (AnimationCounter.Started, AnimationCounter.Running));
        }
        finally
        {
            run.SetResult();
        }
    }
}
