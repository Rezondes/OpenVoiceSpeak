using OVS.Server;
using OVS.Server.Voice;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Server.Voice;

public sealed class SessionVoiceTests : IDisposable
{
    static readonly Guid Channel = Guid.NewGuid(), OtherChannel = Guid.NewGuid();
    static readonly ReadOnlyMemory<byte> Opus = new byte[] { 1, 2, 3 };
    readonly ManualTimeProvider time = new();
    readonly SessionVoice voice;

    public SessionVoiceTests() => voice = new SessionVoice(new byte[32], time);

    public void Dispose() => voice.Dispose();

    [Fact]
    public void New_NothingSeenYet_AllVoiceStateEmpty()
    {
        Assert.Null(voice.UdpEndpoint);
        Assert.False(voice.TcpVoice);
        Assert.Equal(-1, voice.LastUdpFrame);
        Assert.Null(voice.RecoveredFrame);
        Assert.Equal(default, voice.DownlinkLoss);
        Assert.False(voice.WantsCopies);
    }

    [Fact]
    public void Limiter_PastVoiceBurst_Refused()
    {
        for (int i = 0; i < ServerTuning.VoiceBurst; i++) Assert.True(voice.Limiter.TryTake());
        Assert.False(voice.Limiter.TryTake());
    }

    [Fact]
    public void DownlinkLoss_Set_ReadsBackBothHalves()
    {
        voice.DownlinkLoss = new LossReport(uint.MaxValue, 7);
        Assert.Equal(new LossReport(uint.MaxValue, 7), voice.DownlinkLoss);
    }

    [Fact]
    public void WantsCopies_LossAtCopyFromLossPercent_True()
    {
        voice.DownlinkLoss = new LossReport(100, (uint)RelayPayload.CopyFromLossPercent);
        Assert.True(voice.WantsCopies);
    }

    [Fact]
    public void WantsCopies_LossBelowCopyFromLossPercent_False()
    {
        voice.DownlinkLoss = new LossReport(1000, (uint)(RelayPayload.CopyFromLossPercent * 10) - 1);
        Assert.False(voice.WantsCopies);
    }

    [Fact]
    public void WantsCopies_ReportWithNothingExpected_KeepsTheLastDecision()
    {
        voice.DownlinkLoss = new LossReport(10, 5);
        voice.DownlinkLoss = new LossReport(0, 0);
        Assert.True(voice.WantsCopies);
    }

    [Fact]
    public void WantsCopies_ReportAsOldAsCopiesStaleAfter_StillTrue()
    {
        voice.DownlinkLoss = new LossReport(10, 5);
        time.Advance(SessionVoice.CopiesStaleAfter);
        Assert.True(voice.WantsCopies);
    }

    [Fact]
    public void WantsCopies_ReportOlderThanCopiesStaleAfter_False()
    {
        voice.DownlinkLoss = new LossReport(10, 5);
        time.Advance(SessionVoice.CopiesStaleAfter + TimeSpan.FromTicks(1));
        Assert.False(voice.WantsCopies);
    }

    [Fact]
    public void TrackFrame_FirstFrame_NothingKeptNothingUnseen()
    {
        var (kept, unseen) = voice.TrackFrame(5, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        Assert.True(kept.IsEmpty);
        Assert.False(unseen);
    }

    [Fact]
    public void TrackFrame_RightAfterARelayedOne_KeepsIt()
    {
        voice.TrackFrame(5, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        var (kept, unseen) = voice.TrackFrame(6, Channel, VoiceHeader.TargetChannel, new byte[] { 9 }, relayed: true);
        Assert.Equal(Opus.ToArray(), kept.ToArray());
        Assert.False(unseen);
    }

    [Fact]
    public void TrackFrame_OneBeforeNeverArrived_Unseen()
    {
        voice.TrackFrame(5, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        var (kept, unseen) = voice.TrackFrame(7, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        Assert.True(kept.IsEmpty);
        Assert.True(unseen);
    }

    [Fact]
    public void TrackFrame_OneBeforeArrivedButNotRelayed_NeitherKeptNorUnseen()
    {
        voice.TrackFrame(5, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        voice.TrackFrame(6, Channel, VoiceHeader.TargetChannel, Opus, relayed: false);
        var (kept, unseen) = voice.TrackFrame(7, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        Assert.True(kept.IsEmpty);
        Assert.False(unseen);
    }

    [Fact]
    public void TrackFrame_FrameZeroAfterARelayedOne_NeitherKeptNorUnseen()
    {
        voice.TrackFrame(uint.MaxValue, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        var (kept, unseen) = voice.TrackFrame(0, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        Assert.True(kept.IsEmpty);
        Assert.False(unseen);
    }

    /// <summary>Frames 5 and 7 relayed, then 6 relayed late: returns what frame 8 gets.</summary>
    (ReadOnlyMemory<byte> Kept, bool Unseen) TrackAfterALateFrame(ReadOnlyMemory<byte> opusOf7)
    {
        voice.TrackFrame(5, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        voice.TrackFrame(7, Channel, VoiceHeader.TargetChannel, opusOf7, relayed: true);
        voice.TrackFrame(6, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        return voice.TrackFrame(8, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
    }

    [Fact]
    public void TrackFrame_AfterALateOlderFrame_KeepsTheNewestRelayed()
    {
        var opusOf7 = new byte[] { 7, 7 };
        Assert.Equal(opusOf7, TrackAfterALateFrame(opusOf7).Kept.ToArray());
    }

    [Fact]
    public void TrackFrame_AfterALateOlderFrame_NotUnseen()
    {
        Assert.False(TrackAfterALateFrame(new byte[] { 7, 7 }).Unseen);
    }

    [Fact]
    public void TrackFrame_OtherChannel_NothingKept()
    {
        voice.TrackFrame(5, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        var (kept, unseen) = voice.TrackFrame(6, OtherChannel, VoiceHeader.TargetChannel, Opus, relayed: true);
        Assert.True(kept.IsEmpty);
        Assert.False(unseen);
    }

    [Fact]
    public void TrackFrame_OtherTarget_NothingKept()
    {
        voice.TrackFrame(5, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        var (kept, _) = voice.TrackFrame(6, Channel, VoiceHeader.TargetLinked, Opus, relayed: true);
        Assert.True(kept.IsEmpty);
    }

    [Fact]
    public void TrackFrame_AtCopyMaxAge_KeepsIt()
    {
        voice.TrackFrame(5, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        time.Advance(RelayPayload.CopyMaxAge);
        var (kept, _) = voice.TrackFrame(6, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        Assert.False(kept.IsEmpty);
    }

    [Fact]
    public void TrackFrame_OlderThanCopyMaxAge_NothingKept()
    {
        voice.TrackFrame(5, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        time.Advance(RelayPayload.CopyMaxAge + TimeSpan.FromTicks(1));
        var (kept, unseen) = voice.TrackFrame(6, Channel, VoiceHeader.TargetChannel, Opus, relayed: true);
        Assert.True(kept.IsEmpty);
        Assert.False(unseen);
    }

    [Fact]
    public void Dispose_Crypto_Disposed()
    {
        voice.Dispose();
        Assert.Throws<ObjectDisposedException>(() =>
            voice.Crypto.Seal(PacketDirection.ServerToClient, new VoiceHeader(PacketType.Ping, 1, 1, 0), []));
    }
}
