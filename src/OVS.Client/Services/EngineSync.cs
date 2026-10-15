using OVS.Client.Audio;
using OVS.Client.Net;
using OVS.Client.Settings;
using OVS.Shared.Protocol;

namespace OVS.Client.Services;

/// <summary>
/// Keeps the audio engine in step with the rest of the client: the connection's state and the speaker gains it mixes with,
/// and the saved settings (also restarting the devices). It reads the connection and the saved volumes through delegates,
/// so it knows neither the server view model nor the settings page.
/// </summary>
internal sealed class EngineSync
{
    readonly AudioEngine audio;
    readonly Func<EngineConnectionState> connectionState;
    readonly Func<StateMirror?> mirror;
    readonly Func<string, float> volumeFor;
    Guid? bitrateChannel;
    StateMirror? gainsFor;

    /// <param name="audio">The engine to keep in step.</param>
    /// <param name="connectionState">The current connection's state, read on every <see cref="SyncConnection"/>.</param>
    /// <param name="mirror">
    /// The connected server's state (null: none), compared by reference: its speaker gains are built again when it is another
    /// one, as after a reconnect, and its users are the speakers.
    /// </param>
    /// <param name="volumeFor">The saved volume of a user by fingerprint; 1 for the ones never changed.</param>
    internal EngineSync(AudioEngine audio, Func<EngineConnectionState> connectionState, Func<StateMirror?> mirror, Func<string, float> volumeFor)
    {
        this.audio = audio;
        this.connectionState = connectionState;
        this.mirror = mirror;
        this.volumeFor = volumeFor;
    }

    /// <summary>Package 122: how often the speaker gain table was built (tests count them).</summary>
    internal int GainTableBuilds { get; private set; }

    /// <summary>The engine follows the connection: connected, muted, deafened, the speak right, the channel's mute and bitrate; the gains of a server not seen yet.</summary>
    internal void SyncConnection()
    {
        var current = connectionState();
        audio.Connected = current.Connected;
        audio.SelfMuted = current.SelfMuted;
        audio.Deafened = current.Deafened;
        audio.HasSpeakLinked = current.HasSpeakLinked;
        audio.ChannelMuted = current.ChannelMuted;
        // Package 132: join, move, edit; clamped, so a hand-edited server file cannot hand Opus an invalid bitrate
        audio.ChannelBitrate = Math.Clamp(current.ChannelBitrate ?? ProtocolDefaults.Bitrate, ProtocolLimits.MinBitrate, ProtocolLimits.MaxBitrate);
        if (current.ChannelId != bitrateChannel)
        {
            bitrateChannel = current.ChannelId;
            audio.Loss.Reset(audio.ChannelBitrate); // Package 135: a move starts at the channel's bitrate, also at the same one
        }
        if (mirror() != gainsFor) SyncGains();
    }

    /// <summary>Package 51: the volume per person, mapped to the session ids of this connection (they change on reconnect).</summary>
    internal void SyncGains()
    {
        gainsFor = mirror();
        GainTableBuilds++;
        audio.Mixer.SetSpeakerGains((gainsFor?.Users.Values.AsEnumerable() ?? [])
            .Select(user => (user.SessionId, Volume: volumeFor(user.Fingerprint)))
            .Where(speaker => speaker.Volume != 1f)
            .ToDictionary(speaker => speaker.SessionId, speaker => speaker.Volume));
    }

    /// <summary>
    /// Users came or went: the gain table follows, and Package 124: a speaker's decoder stays over talk pauses, so it goes when
    /// the speaker left. The self test's own voice is no user and stays. Without a server state it only builds the empty gain
    /// table: a guard the current caller never reaches (it asks after a member change of a shown server).
    /// </summary>
    internal void SyncMembers()
    {
        SyncGains();
        if (gainsFor is not { } current) return;
        var users = current.Users;
        audio.Mixer.RemoveLeft(id => id == AudioEngine.SelfTestSpeaker || users.ContainsKey(id));
    }

    /// <summary>Package 174: a connection is being made (or no longer), so the speaker is open before the "Verbunden" sound.</summary>
    internal void SetConnecting(bool connecting) => audio.Connecting = connecting;

    /// <summary>The connection is gone: nobody is heard any more, so every speaker's decoder goes.</summary>
    internal void ClearSpeakers() => audio.Mixer.Clear();

    /// <summary>Applies saved settings to the engine, restarting its devices; returns what to warn about, null when nothing.</summary>
    internal string? Configure(ClientSettings applied) => audio.Configure(applied);

    /// <summary>
    /// Package 98: the same, with the devices restarted off the UI thread (the engine locks itself). Without devices
    /// nothing takes long, so it all happens at once.
    /// </summary>
    internal Task<string?> ConfigureAsync(ClientSettings applied) =>
        audio.DeviceLifecycle.OpensDevices ? Task.Run(() => audio.Configure(applied)) : Task.FromResult(audio.Configure(applied));
}
