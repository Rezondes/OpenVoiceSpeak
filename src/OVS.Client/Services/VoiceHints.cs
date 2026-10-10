using OVS.Client.Localization;

namespace OVS.Client.Services;

/// <summary>
/// The hint under the own name that says why nobody hears the user: the own channel is muted, there is no right to speak into
/// the linked channels, or a talk key (or the voice gate) tries to talk into a muted microphone. It reads the server's state,
/// the keys and the engine's voice gate through delegates, so it knows neither the view models nor the engine.
/// </summary>
internal sealed class VoiceHints
{
    /// <summary>How long a talk attempt into a muted microphone keeps its hint on.</summary>
    static readonly TimeSpan MutedHintHold = TimeSpan.FromSeconds(2);

    readonly Func<VoiceHintState?> state;
    readonly Func<bool> linkKeyDown;
    readonly Func<bool> muteKeyHeld;
    readonly Func<bool> talkKeyHeld;
    readonly Func<bool> inVoiceActivation;
    readonly Func<bool> voiceDetected;
    readonly TimeProvider time;
    DateTimeOffset talkedIntoMuteAt = DateTimeOffset.MinValue;

    /// <param name="state">The connected server and the own state on it; null when none is connected.</param>
    /// <param name="linkKeyDown">The link push-to-talk key is down (with its release delay).</param>
    /// <param name="muteKeyHeld">Push-to-mute is held, which is muting on purpose.</param>
    /// <param name="talkKeyHeld">A talk key is held.</param>
    /// <param name="inVoiceActivation">The engine is in voice activation mode.</param>
    /// <param name="voiceDetected">The voice gate hears voice.</param>
    /// <param name="time">The clock the hold of <see cref="MutedHintHold"/> is measured on.</param>
    internal VoiceHints(Func<VoiceHintState?> state, Func<bool> linkKeyDown, Func<bool> muteKeyHeld, Func<bool> talkKeyHeld,
        Func<bool> inVoiceActivation, Func<bool> voiceDetected, TimeProvider time)
    {
        this.state = state;
        this.linkKeyDown = linkKeyDown;
        this.muteKeyHeld = muteKeyHeld;
        this.talkKeyHeld = talkKeyHeld;
        this.inVoiceActivation = inVoiceActivation;
        this.voiceDetected = voiceDetected;
        this.time = time;
    }

    /// <summary>The hint for right now, "" for none. Called on every UI tick; the hold of a talk attempt runs on <c>time</c>.</summary>
    internal string Evaluate() => state() switch
    {
        { ChannelMuted: true } => Strings.VoiceHint_MutedChannel,
        { HasSpeakLinked: false } when linkKeyDown() => Strings.VoiceHint_NoLinkRight,
        { } current => ExplainMutedTalk(current),
        _ => "",
    };

    /// <summary>
    /// Package 131 (A140): talking into a muted microphone (a talk key held, or voice activation hears voice) shows why
    /// nobody hears it, until 2 s after. Push-to-mute held is muting on purpose and shows nothing.
    /// </summary>
    string ExplainMutedTalk(VoiceHintState current)
    {
        string why = current.ServerMuted ? Strings.VoiceHint_ServerMuted
            : current.SelfDeafened ? Strings.VoiceHint_Deafened
            : current.SelfMuted && !muteKeyHeld() ? Strings.VoiceHint_SelfMuted
            : "";
        if (why == "")
        {
            talkedIntoMuteAt = DateTimeOffset.MinValue; // muting again later must not bring back an old attempt
            return "";
        }
        var now = time.GetUtcNow();
        if (talkKeyHeld() || (inVoiceActivation() && voiceDetected())) talkedIntoMuteAt = now;
        return now - talkedIntoMuteAt < MutedHintHold ? why : "";
    }
}
