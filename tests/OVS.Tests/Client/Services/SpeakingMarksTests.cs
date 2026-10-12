using OVS.Client.Audio.Playback;
using OVS.Client.Services;
using OVS.Shared.Voice;
using OVS.Tests.TestSupport;

namespace OVS.Tests.Client.Services;

/// <summary>Packages 57, 60 and 122 without a server: who is shown speaking and when a link tone is due.</summary>
public sealed class SpeakingMarksTests
{
    const uint Me = 1, Anna = 2, Bert = 3;

    readonly ManualTimeProvider time = new();
    readonly SpeakingMarks marks;

    public SpeakingMarksTests()
    {
        marks = new SpeakingMarks(time);
    }

    static ActiveSpeaker Heard(uint sessionId, bool viaLink = false) => new(sessionId, viaLink);

    SpeakingState StateOf(uint sessionId) => marks.Refresh(Me).Single(state => state.SessionId == sessionId);

    [Fact]
    public void Refresh_299msAfterAReport_StillSpeaking()
    {
        marks.Hear(Heard(Anna), isOtherKnownUser: true);
        time.Advance(TimeSpan.FromMilliseconds(299));

        Assert.Equal(new SpeakingState(Anna, true, false), StateOf(Anna));
    }

    [Fact]
    public void Refresh_300msAfterAReport_NoLongerSpeaking()
    {
        marks.Hear(Heard(Anna), isOtherKnownUser: true);
        marks.Refresh(Me);
        time.Advance(TimeSpan.FromMilliseconds(300));

        Assert.Equal(new SpeakingState(Anna, false, false), StateOf(Anna));
    }

    [Fact]
    public void Refresh_StoppedSpeaker_IsOffOnceThenNoLongerListed()
    {
        marks.Hear(Heard(Anna), isOtherKnownUser: true);
        marks.Refresh(Me);
        time.Advance(SpeakingMarks.SpeakingHold);
        marks.Refresh(Me);

        Assert.Equal([Me], marks.Refresh(Me).Select(state => state.SessionId));
    }

    [Fact]
    public void Hear_FirstViaLink_LinkToneDue() => Assert.True(marks.Hear(Heard(Anna, viaLink: true), isOtherKnownUser: true));

    [Fact]
    public void Hear_AgainWithin300ms_NoTone()
    {
        marks.Hear(Heard(Anna, viaLink: true), isOtherKnownUser: true);
        time.Advance(SpeakingMarks.LinkPause);

        Assert.False(marks.Hear(Heard(Anna, viaLink: true), isOtherKnownUser: true));
    }

    [Fact]
    public void Hear_AgainAfterMoreThan300ms_ToneDue()
    {
        marks.Hear(Heard(Anna, viaLink: true), isOtherKnownUser: true);
        time.Advance(SpeakingMarks.LinkPause + TimeSpan.FromMilliseconds(1));

        Assert.True(marks.Hear(Heard(Anna, viaLink: true), isOtherKnownUser: true));
    }

    [Fact]
    public void Hear_NotViaLink_NoTone() => Assert.False(marks.Hear(Heard(Anna), isOtherKnownUser: true));

    [Fact]
    public void Hear_SelfOrUnknownUser_NoToneButShown()
    {
        Assert.False(marks.Hear(Heard(Bert, viaLink: true), isOtherKnownUser: false));

        Assert.Equal(new SpeakingState(Bert, true, true), StateOf(Bert));
    }

    [Fact]
    public void Hear_SameSpeakerTwiceInOneReport_OneTone()
    {
        var tones = new[] { Heard(Anna, viaLink: true), Heard(Anna, viaLink: true) }.Count(speaker => marks.Hear(speaker, isOtherKnownUser: true));

        Assert.Equal(1, tones);
    }

    [Fact]
    public void SetOwnTarget_LinkedInALinkedChannel_OwnToneDue() => Assert.True(marks.SetOwnTarget(VoiceHeader.TargetLinked, ownChannelLinked: true));

    [Fact]
    public void SetOwnTarget_LinkedInAnUnlinkedChannel_NoTone() => Assert.False(marks.SetOwnTarget(VoiceHeader.TargetLinked, ownChannelLinked: false));

    [Fact]
    public void SetOwnTarget_LinkedAgainWithin300msOfTheEnd_NoTone()
    {
        marks.SetOwnTarget(VoiceHeader.TargetLinked, ownChannelLinked: true);
        marks.SetOwnTarget(null, ownChannelLinked: true);
        time.Advance(SpeakingMarks.LinkPause);

        Assert.False(marks.SetOwnTarget(VoiceHeader.TargetLinked, ownChannelLinked: true));
    }

    [Fact]
    public void SetOwnTarget_LinkedAgainAfterMoreThan300ms_ToneDue()
    {
        marks.SetOwnTarget(VoiceHeader.TargetLinked, ownChannelLinked: true);
        marks.SetOwnTarget(null, ownChannelLinked: true);
        time.Advance(SpeakingMarks.LinkPause + TimeSpan.FromMilliseconds(1));

        Assert.True(marks.SetOwnTarget(VoiceHeader.TargetLinked, ownChannelLinked: true));
    }

    [Fact]
    public void SetOwnTarget_SameTargetTwice_NoSecondTone()
    {
        marks.SetOwnTarget(VoiceHeader.TargetLinked, ownChannelLinked: true);

        Assert.False(marks.SetOwnTarget(VoiceHeader.TargetLinked, ownChannelLinked: true));
    }

    [Fact]
    public void Refresh_OwnTargetLinked_SelfShownViaLink()
    {
        marks.SetOwnTarget(VoiceHeader.TargetLinked, ownChannelLinked: true);

        Assert.Equal(new SpeakingState(Me, true, true), StateOf(Me));
    }

    [Fact]
    public void Refresh_Silent_ListsOnlyTheOwnSessionOff() => Assert.Equal([new SpeakingState(Me, false, false)], marks.Refresh(Me));
}
