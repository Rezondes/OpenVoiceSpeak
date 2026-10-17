namespace OVS.Client.Audio.Sounds;

/// <summary>
/// Package 131 (A140): the talk key tones. Package 189 (A201): they play on the edges of "a held talk key sends", which
/// both the key thread (a key) and the UI thread (a mute, the channel's mute, a link) can move.
/// </summary>
internal sealed class TalkKeyTone
{
    readonly Func<bool> sends;
    readonly Func<bool> canPlay;
    readonly SoundPlayer sounds;
    readonly object gate = new();
    bool sending; // under gate

    /// <param name="sends">A talk key is held and its frames would be sent.</param>
    /// <param name="canPlay">The keys can talk now (connected or in the self test).</param>
    /// <param name="sounds">Where the tones play.</param>
    public TalkKeyTone(Func<bool> sends, Func<bool> canPlay, SoundPlayer sounds)
    {
        this.sends = sends;
        this.canPlay = canPlay;
        this.sounds = sounds;
    }

    /// <summary>Plays the down tone when sending began, the up tone when it ended, nothing otherwise.</summary>
    public void Refresh()
    {
        lock (gate)
        {
            bool now = sends();
            if (now == sending) return;
            sending = now;
            if (canPlay()) sounds.Play(now ? SoundEvent.TalkKeyDown : SoundEvent.TalkKeyUp);
        }
    }
}
