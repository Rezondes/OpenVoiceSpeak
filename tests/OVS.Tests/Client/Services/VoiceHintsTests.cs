using OVS.Client.Localization;
using OVS.Client.Services;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>
/// The hint under the own name that says why nobody hears the user, without a window, a
/// connection or a key poller. The server's state, the keys and the engine's voice gate are what the fields hold when it asks.
/// </summary>
public sealed class VoiceHintsTests
{
    static readonly VoiceHintState Healthy = new(ChannelMuted: false, HasSpeakLinked: true, ServerMuted: false, SelfDeafened: false, SelfMuted: false);

    readonly ManualTimeProvider clock = new();
    readonly VoiceHints hints;
    VoiceHintState? state = Healthy;
    bool linkKeyDown;
    bool muteKeyHeld;
    bool talkKeyHeld;
    bool inVoiceActivation;
    bool voiceDetected;

    public VoiceHintsTests()
    {
        hints = new VoiceHints(state: () => state, linkKeyDown: () => linkKeyDown, muteKeyHeld: () => muteKeyHeld, talkKeyHeld: () => talkKeyHeld,
            inVoiceActivation: () => inVoiceActivation, voiceDetected: () => voiceDetected, time: clock);
    }

    // ---- the hints that come first: the channel and the link right ----

    [Fact]
    public void Evaluate_NoServer_ShowsNothing()
    {
        state = null;
        linkKeyDown = true;
        talkKeyHeld = true;

        Assert.Equal("", hints.Evaluate());
    }

    [Fact]
    public void Evaluate_ChannelMuted_ShowsTheMutedChannelHint()
    {
        state = Healthy with { ChannelMuted = true };

        Assert.Equal(Strings.VoiceHint_MutedChannel, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_ChannelMutedWithEveryOtherReasonToo_ShowsTheMutedChannelHint()
    {
        state = new VoiceHintState(ChannelMuted: true, HasSpeakLinked: false, ServerMuted: true, SelfDeafened: true, SelfMuted: true);
        linkKeyDown = true;
        talkKeyHeld = true;

        Assert.Equal(Strings.VoiceHint_MutedChannel, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_NoLinkRightWithLinkKeyDown_ShowsTheNoLinkRightHint()
    {
        state = Healthy with { HasSpeakLinked = false };
        linkKeyDown = true;

        Assert.Equal(Strings.VoiceHint_NoLinkRight, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_NoLinkRightWithoutLinkKeyDown_ShowsNothing()
    {
        state = Healthy with { HasSpeakLinked = false };

        Assert.Equal("", hints.Evaluate());
    }

    [Fact]
    public void Evaluate_LinkRightWithLinkKeyDown_ShowsNothing()
    {
        linkKeyDown = true;

        Assert.Equal("", hints.Evaluate());
    }

    [Fact]
    public void Evaluate_NoLinkRightWithLinkKeyDownAndSelfMutedTalking_ShowsTheNoLinkRightHint()
    {
        state = Healthy with { HasSpeakLinked = false, SelfMuted = true };
        linkKeyDown = true;
        talkKeyHeld = true;

        Assert.Equal(Strings.VoiceHint_NoLinkRight, hints.Evaluate());
    }

    // ---- talking into a muted microphone: which reason ----

    [Fact]
    public void Evaluate_ServerMutedAndTalkKeyHeld_ShowsTheServerMutedHint()
    {
        state = Healthy with { ServerMuted = true };
        talkKeyHeld = true;

        Assert.Equal(Strings.VoiceHint_ServerMuted, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_DeafenedAndTalkKeyHeld_ShowsTheDeafenedHint()
    {
        state = Healthy with { SelfDeafened = true };
        talkKeyHeld = true;

        Assert.Equal(Strings.VoiceHint_Deafened, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_SelfMutedAndTalkKeyHeld_ShowsTheSelfMutedHint()
    {
        state = Healthy with { SelfMuted = true };
        talkKeyHeld = true;

        Assert.Equal(Strings.VoiceHint_SelfMuted, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_ServerMutedAndDeafenedAndSelfMutedAndTalkKeyHeld_ShowsTheServerMutedHint()
    {
        state = Healthy with { ServerMuted = true, SelfDeafened = true, SelfMuted = true };
        talkKeyHeld = true;

        Assert.Equal(Strings.VoiceHint_ServerMuted, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_DeafenedAndSelfMutedAndTalkKeyHeld_ShowsTheDeafenedHint()
    {
        state = Healthy with { SelfDeafened = true, SelfMuted = true };
        talkKeyHeld = true;

        Assert.Equal(Strings.VoiceHint_Deafened, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_SelfMutedWhilePushToMuteIsHeld_ShowsNothing()
    {
        state = Healthy with { SelfMuted = true };
        muteKeyHeld = true;
        talkKeyHeld = true;

        Assert.Equal("", hints.Evaluate());
    }

    [Fact]
    public void Evaluate_ServerMutedWhilePushToMuteIsHeld_ShowsTheServerMutedHint()
    {
        state = Healthy with { ServerMuted = true };
        muteKeyHeld = true;
        talkKeyHeld = true;

        Assert.Equal(Strings.VoiceHint_ServerMuted, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_DeafenedWhilePushToMuteIsHeld_ShowsTheDeafenedHint()
    {
        state = Healthy with { SelfDeafened = true };
        muteKeyHeld = true;
        talkKeyHeld = true;

        Assert.Equal(Strings.VoiceHint_Deafened, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_NotMutedAndTalkKeyHeld_ShowsNothing()
    {
        talkKeyHeld = true;

        Assert.Equal("", hints.Evaluate());
    }

    // ---- talking into a muted microphone: what counts as talking ----

    [Fact]
    public void Evaluate_MutedWithoutTalking_ShowsNothing()
    {
        state = Healthy with { SelfMuted = true };

        Assert.Equal("", hints.Evaluate());
    }

    [Fact]
    public void Evaluate_MutedAndVoiceDetectedInVoiceActivation_ShowsTheSelfMutedHint()
    {
        state = Healthy with { SelfMuted = true };
        inVoiceActivation = true;
        voiceDetected = true;

        Assert.Equal(Strings.VoiceHint_SelfMuted, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_MutedAndVoiceDetectedInPushToTalk_ShowsNothing()
    {
        state = Healthy with { SelfMuted = true };
        voiceDetected = true;

        Assert.Equal("", hints.Evaluate());
    }

    [Fact]
    public void Evaluate_MutedInVoiceActivationWithoutVoice_ShowsNothing()
    {
        state = Healthy with { SelfMuted = true };
        inVoiceActivation = true;

        Assert.Equal("", hints.Evaluate());
    }

    // ---- talking into a muted microphone: how long it stays ----

    void TalkIntoMutedMicrophone()
    {
        state = Healthy with { SelfMuted = true };
        talkKeyHeld = true;
        hints.Evaluate();
        talkKeyHeld = false;
    }

    [Fact]
    public void Evaluate_TalkEndedJustUnderTwoSecondsAgo_StillShowsTheHint()
    {
        TalkIntoMutedMicrophone();
        clock.Advance(TimeSpan.FromMilliseconds(1999));

        Assert.Equal(Strings.VoiceHint_SelfMuted, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_TalkEndedTwoSecondsAgo_ShowsNothing()
    {
        TalkIntoMutedMicrophone();
        clock.Advance(TimeSpan.FromSeconds(2));

        Assert.Equal("", hints.Evaluate());
    }

    [Fact]
    public void Evaluate_TalkGoesOnForFiveSeconds_KeepsShowingTheHint()
    {
        state = Healthy with { SelfMuted = true };
        talkKeyHeld = true;
        hints.Evaluate();
        clock.Advance(TimeSpan.FromSeconds(5));

        Assert.Equal(Strings.VoiceHint_SelfMuted, hints.Evaluate());
    }

    [Fact]
    public void Evaluate_MutedAgainAfterBeingUnmuted_DoesNotBringBackTheOldAttempt()
    {
        TalkIntoMutedMicrophone();
        state = Healthy;
        hints.Evaluate();
        clock.Advance(TimeSpan.FromMilliseconds(500));
        state = Healthy with { SelfMuted = true };

        Assert.Equal("", hints.Evaluate());
    }

    [Fact]
    public void Evaluate_MutedAfterTalkingUnmuted_ShowsNothing()
    {
        talkKeyHeld = true;
        hints.Evaluate();
        talkKeyHeld = false;
        clock.Advance(TimeSpan.FromMilliseconds(500));
        state = Healthy with { SelfMuted = true };

        Assert.Equal("", hints.Evaluate());
    }
}
