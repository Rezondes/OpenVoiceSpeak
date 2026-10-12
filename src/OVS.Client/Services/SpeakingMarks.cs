using OVS.Client.Audio.Playback;
using OVS.Shared.Voice;

namespace OVS.Client.Services;

/// <summary>
/// Who is shown speaking (Package 122) and when a link tone is due (Packages 57 and 60), on the injected clock: a speaker
/// the engine reported stays shown for 300 ms; someone talking in over a link gets a tone when they start after a pause
/// longer than 300 ms, and the own voice gets one when it starts going over a link of the own channel after such a pause.
/// The tones are played by the caller; this class only decides.
/// </summary>
internal sealed class SpeakingMarks
{
    /// <summary>How long a reported speaker stays shown speaking.</summary>
    internal static readonly TimeSpan SpeakingHold = TimeSpan.FromMilliseconds(300);

    /// <summary>Package 57: a pause longer than this starts a new link transmission, with a new tone.</summary>
    internal static readonly TimeSpan LinkPause = TimeSpan.FromMilliseconds(300);

    readonly TimeProvider time;
    readonly Dictionary<uint, HeardSpeaker> speaking = [];
    readonly Dictionary<uint, DateTimeOffset> lastLinkVoice = [];
    /// <summary>The sessions shown speaking; only they, the own one and those reported since can change.</summary>
    readonly HashSet<uint> shown = [];
    byte? ownTarget;
    DateTimeOffset? ownLinkEnded;

    /// <param name="time">The client's one clock.</param>
    internal SpeakingMarks(TimeProvider time)
    {
        this.time = time;
    }

    /// <summary>
    /// The engine heard <paramref name="speaker"/>; true when a link tone is due: the speaker talks in over a link, is
    /// another known user (<paramref name="isOtherKnownUser"/>), and was not heard over a link within the last pause.
    /// </summary>
    internal bool Hear(ActiveSpeaker speaker, bool isOtherKnownUser)
    {
        var now = time.GetUtcNow();
        speaking[speaker.SessionId] = new HeardSpeaker(now, speaker.ViaLink);
        // Someone from another channel talks in over a link: without a tone it sounds like the own channel.
        if (!speaker.ViaLink || !isOtherKnownUser) return false;
        var due = !lastLinkVoice.TryGetValue(speaker.SessionId, out var last) || now - last > LinkPause;
        lastLinkVoice[speaker.SessionId] = now;
        return due;
    }

    /// <summary>
    /// Package 60: the own voice now goes to <paramref name="target"/> (null: silent); true when the own link tone is due:
    /// it starts going over a link of the own channel (<paramref name="ownChannelLinked"/>) after a pause longer than the link pause.
    /// </summary>
    internal bool SetOwnTarget(byte? target, bool ownChannelLinked)
    {
        const byte Linked = VoiceHeader.TargetLinked;
        var now = time.GetUtcNow();
        var due = target == Linked && ownTarget != Linked && ownChannelLinked && (ownLinkEnded is not { } ended || now - ended > LinkPause);
        if (ownTarget == Linked && target != Linked) ownLinkEnded = now;
        ownTarget = target;
        return due;
    }

    /// <summary>
    /// The states of the sessions whose shown speaking may have changed: those shown, those reported since and the own one
    /// (<paramref name="selfId"/>). A speaker no longer heard within the hold is off once, then no longer listed.
    /// </summary>
    internal List<SpeakingState> Refresh(uint selfId)
    {
        var now = time.GetUtcNow();
        foreach (var gone in speaking.Where(entry => now - entry.Value.At >= SpeakingHold).Select(entry => entry.Key).ToList()) speaking.Remove(gone);
        var states = new List<SpeakingState>();
        foreach (var id in shown.Union(speaking.Keys).Append(selfId).ToList())
        {
            var state = id == selfId
                ? new SpeakingState(id, ownTarget is not null, ownTarget == VoiceHeader.TargetLinked)
                : speaking.TryGetValue(id, out var heard) ? new SpeakingState(id, true, heard.ViaLink) : new SpeakingState(id, false, false);
            if (state.On) shown.Add(id);
            else shown.Remove(id);
            states.Add(state);
        }
        return states;
    }

    /// <summary>When a speaker was last heard, and whether over a link.</summary>
    readonly record struct HeardSpeaker(DateTimeOffset At, bool ViaLink);
}
