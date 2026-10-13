using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using OVS.Client.Animations;
using OVS.Client.Models;
using OVS.Client.Settings;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Animations;

/// <summary>Package 108: the slot counter ticks to its new numbers and pulses once when the channel gets full.</summary>
[Collection(MotionCollection.Name)]
public sealed class SlotCounterTests
{
    /// <summary>A slot counter alone in a window of <paramref name="display"/>, with no numbers yet.</summary>
    static CounterScene Counter(DisplayMode display)
    {
        var counter = new TextBlock();
        var window = new Window { Width = 200, Height = 100, Content = counter };
        window.Show();
        Motion.Apply(window, display);
        Dispatcher.UIThread.RunJobs();
        return new(window, counter);
    }

    static bool AtRest(TextBlock counter) => (MotionWait.OffsetY(counter), MotionWait.Scale(counter), counter.Opacity) == (0d, 1d, 1d);

    [AvaloniaTheory]
    [InlineData(1, 3, 2, 3, 1)] // one more, not full: the tick
    [InlineData(2, 3, 1, 3, 1)] // one less
    [InlineData(2, 3, 3, 3, 2)] // full now: the tick and the pulse
    [InlineData(3, 3, 4, 3, 1)] // full before (one with the right to join a full channel): no second pulse
    [InlineData(3, 0, 3, 3, 2)] // a limit that is reached at once
    [InlineData(1, 0, 1, 3, 1)] // a limit that is not reached
    public void Slots_NumbersChange_TickAndPulseOnlyWhenItGetsFullThenRest(int users, int max, int newUsers, int newMax, int animations)
    {
        var (window, counter) = Counter(DisplayMode.Animated);
        try
        {
            LiveMotion.SetSlots(counter, new ChannelSlots(users, max));
            int running = AnimationCounter.Running, started = AnimationCounter.Started;
            LiveMotion.SetSlots(counter, new ChannelSlots(newUsers, newMax));
            Assert.Equal(started + animations, AnimationCounter.Started);
            MotionWait.Until(() => AnimationCounter.Running <= running && AtRest(counter)); // the tick and pulse have ended
            Assert.True(AtRest(counter), $"at rest ({MotionWait.OffsetY(counter)}, {MotionWait.Scale(counter)}, {counter.Opacity})");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(1, 2, true)]
    [InlineData(2, 1, false)]
    public void Slots_UserCountChanges_HigherComesFromBelowLowerFromAbove(int users, int newUsers, bool fromBelow)
    {
        var (window, counter) = Counter(DisplayMode.Animated);
        try
        {
            LiveMotion.SetSlots(counter, new ChannelSlots(users, 3));
            var offsets = MotionWait.Record(counter, MotionWait.OffsetY); // every value as it happens: under load one frame can outlast the tick
            int running = AnimationCounter.Running;
            LiveMotion.SetSlots(counter, new ChannelSlots(newUsers, 3));
            MotionWait.Until(() => AnimationCounter.Running <= running && AtRest(counter)); // the tick has ended
            Assert.True(fromBelow ? offsets.Any(offset => offset > 1) : offsets.Any(offset => offset < -1), $"offsets {string.Join(" ", offsets.Distinct())}");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Slots_FirstNumbers_StartNoAnimation()
    {
        var (window, counter) = Counter(DisplayMode.Animated);
        try
        {
            int started = AnimationCounter.Started;
            LiveMotion.SetSlots(counter, new ChannelSlots(3, 3)); // a row that is built: nothing to tick from
            Assert.Equal(started, AnimationCounter.Started);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Slots_Simplified_StartNoAnimation()
    {
        var (window, counter) = Counter(DisplayMode.Simplified);
        try
        {
            LiveMotion.SetSlots(counter, new ChannelSlots(2, 3));
            int started = AnimationCounter.Started;
            LiveMotion.SetSlots(counter, new ChannelSlots(3, 3));
            Assert.Equal(started, AnimationCounter.Started);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void Slots_CounterInNoWindow_StartNoAnimation()
    {
        var counter = new TextBlock();
        LiveMotion.SetSlots(counter, new ChannelSlots(2, 3));
        int started = AnimationCounter.Started;
        LiveMotion.SetSlots(counter, new ChannelSlots(3, 3));
        Assert.Equal(started, AnimationCounter.Started);
    }

    sealed record CounterScene(Window Window, TextBlock Counter);
}
