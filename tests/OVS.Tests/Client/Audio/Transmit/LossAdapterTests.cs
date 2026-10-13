using OVS.Client.Audio.Transmit;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Audio.Transmit;

public class LossAdapterTests
{
    static LossReport Loss(double percent) => new(1000, (uint)Math.Round(percent * 10));
    static readonly TimeSpan Ping = TimeSpan.FromSeconds(2);

    /// <summary>Package 135 (AC1, A144): the loss Opus is told to expect is clamp(round(1.5 x loss), 5, 30).</summary>
    [Fact]
    public void LossHint_FromLoss()
    {
        Assert.Equal(5, LossAdapter.HintFor(0));
        Assert.Equal(5, LossAdapter.HintFor(3));
        Assert.Equal(15, LossAdapter.HintFor(10));
        Assert.Equal(30, LossAdapter.HintFor(20));
        Assert.Equal(30, LossAdapter.HintFor(40));

        var adapter = new LossAdapter(new ManualTimeProvider(), 64);
        Assert.Equal(10, adapter.LossHint); // before the first figure: what Opus was told before Package 135
        adapter.Report(Loss(0));
        Assert.Equal(5, adapter.LossHint);
        adapter.Report(Loss(10));
        Assert.Equal(15, adapter.LossHint);
        adapter.Report(Loss(40));
        Assert.Equal(30, adapter.LossHint);
        adapter.Report(default); // nothing sent in the window: no figure, the hint stays
        Assert.Equal(30, adapter.LossHint);
    }

    /// <summary>
    /// Package 135 (AC2, A144): above 10 % loss for 5 s the bitrate halves down to 16; 10 s below 2 % it doubles again, at
    /// most to the channel's bitrate. A window with nothing sent (a talk pause) is neither loss nor recovery.
    /// </summary>
    [Fact]
    public void Bitrate_StepsDownAndUp_WithinBounds()
    {
        var time = new ManualTimeProvider();
        var adapter = new LossAdapter(time, 64);
        void Every2s(LossReport loss, int times)
        {
            for (int index = 0; index < times; index++)
            {
                time.Advance(Ping);
                adapter.Report(loss);
            }
        }

        Every2s(Loss(15), 3); // at 2, 4 and 6 s: the first bad figure at 2 s, so 4 s of loss by 6 s
        Assert.Equal(64, adapter.BitrateKbps);
        Every2s(Loss(15), 1); // 6 s of loss
        Assert.Equal(32, adapter.BitrateKbps);
        Every2s(Loss(15), 2); // 4 s since the step
        Assert.Equal(32, adapter.BitrateKbps);
        Every2s(Loss(15), 1);
        Assert.Equal(16, adapter.BitrateKbps);
        Every2s(Loss(50), 10);
        Assert.Equal(16, adapter.BitrateKbps); // never below 16

        Every2s(Loss(5), 10); // between 2 and 10 %: stays
        Assert.Equal(16, adapter.BitrateKbps);
        Every2s(Loss(1), 5); // 8 s of a clean line
        Assert.Equal(16, adapter.BitrateKbps);
        Every2s(Loss(1), 1); // 10 s
        Assert.Equal(32, adapter.BitrateKbps);
        Every2s(Loss(1), 4); // 8 s since the step
        Assert.Equal(32, adapter.BitrateKbps);
        Every2s(Loss(1), 1);
        Assert.Equal(64, adapter.BitrateKbps);
        Every2s(Loss(0), 20);
        Assert.Equal(64, adapter.BitrateKbps); // never above the channel's

        // talk pauses: windows with nothing sent break a streak instead of adding to it
        Every2s(Loss(15), 2);
        Every2s(default, 5);
        Every2s(Loss(15), 2);
        Assert.Equal(64, adapter.BitrateKbps);
        Every2s(Loss(15), 2);
        Assert.Equal(32, adapter.BitrateKbps);
        Every2s(Loss(0), 4);
        Every2s(default, 10);
        Every2s(Loss(0), 4);
        Assert.Equal(32, adapter.BitrateKbps);

        // an odd channel bitrate: halved and capped on the way back
        adapter.Reset(96);
        Assert.Equal(96, adapter.BitrateKbps);
        Every2s(Loss(20), 4);
        Assert.Equal(48, adapter.BitrateKbps);
        Every2s(Loss(0), 6);
        Assert.Equal(96, adapter.BitrateKbps);
        adapter.Reset(24);
        Every2s(Loss(20), 4);
        Assert.Equal(16, adapter.BitrateKbps);
        Every2s(Loss(0), 6);
        Assert.Equal(24, adapter.BitrateKbps);
    }

    /// <summary>
    /// Package 135 (AC2): a gap without figures (an outage, a reconnect) ends both streaks, so one figure after it steps
    /// neither up nor down; so does a new connection.
    /// </summary>
    [Fact]
    public void Gap_EndsStreaks()
    {
        var time = new ManualTimeProvider();
        var adapter = new LossAdapter(time, 64);
        void Figure(double percent, double afterSeconds)
        {
            time.Advance(TimeSpan.FromSeconds(afterSeconds));
            adapter.Report(Loss(percent));
        }

        for (int index = 0; index < 4; index++) Figure(20, 2);
        Assert.Equal(32, adapter.BitrateKbps);
        Figure(0, 2); // a clean streak begins
        Figure(0, 20); // the next figure only after an outage
        Assert.Equal(32, adapter.BitrateKbps);
        Figure(0, 2);
        Figure(0, 2);
        Figure(0, 2); // 6 s of the new streak
        Assert.Equal(32, adapter.BitrateKbps);

        Figure(20, 2); // a lossy streak begins
        Figure(20, 20);
        Assert.Equal(32, adapter.BitrateKbps);

        Figure(20, 2);
        adapter.EndStreaks(); // reconnected
        Figure(20, 4);
        Assert.Equal(32, adapter.BitrateKbps);
        Figure(20, 2);
        Figure(20, 2); // 4 s since the first figure of the new connection
        Assert.Equal(32, adapter.BitrateKbps);
        Figure(20, 2);
        Assert.Equal(16, adapter.BitrateKbps);
    }

    /// <summary>Package 135 (AC2, AC3): an edit of the own channel keeps a lowered bitrate lowered, at most the new value.</summary>
    [Fact]
    public void Edit_SetsCeiling_KeepsLowered()
    {
        var time = new ManualTimeProvider();
        var adapter = new LossAdapter(time, 64);
        adapter.SetCeiling(96); // not lowered: follows the edit
        Assert.Equal(96, adapter.BitrateKbps);
        for (int index = 0; index < 4; index++)
        {
            time.Advance(Ping);
            adapter.Report(Loss(20));
        }
        Assert.Equal(48, adapter.BitrateKbps);
        adapter.SetCeiling(128); // up: stays lowered
        Assert.Equal(48, adapter.BitrateKbps);
        adapter.SetCeiling(32); // down below it: the new value
        Assert.Equal(32, adapter.BitrateKbps);
        for (int index = 0; index < 6; index++)
        {
            time.Advance(Ping);
            adapter.Report(Loss(0));
        }
        Assert.Equal(32, adapter.BitrateKbps); // never above the edited ceiling
    }
}
