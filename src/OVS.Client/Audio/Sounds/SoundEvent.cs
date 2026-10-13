namespace OVS.Client.Audio.Sounds;

/// <summary>Package 47 (A46): the moments the client gives an acoustic cue for.</summary>
#pragma warning disable CA1515 // public: tests take it as a theory parameter, and xunit test methods are public
public enum SoundEvent
#pragma warning restore CA1515
{
    /// <summary>The own microphone was muted.</summary>
    MicOff,
    /// <summary>The own microphone was unmuted.</summary>
    MicOn,
    /// <summary>The own sound was deafened.</summary>
    SoundOff,
    /// <summary>The own sound came back.</summary>
    SoundOn,
    /// <summary>Connected to a server.</summary>
    Connected,
    /// <summary>The connection ended.</summary>
    Disconnected,
    /// <summary>The own user entered a channel.</summary>
    ChannelEntered,
    /// <summary>Someone came into the own channel.</summary>
    UserJoined,
    /// <summary>Someone left the own channel.</summary>
    UserLeft,
    /// <summary>A moderator muted the own user at the server.</summary>
    ServerMuted,
    /// <summary>A moderator moved the own user.</summary>
    Moved,
    /// <summary>A private message arrived.</summary>
    PrivateMessage,
    /// <summary>A message in "Allgemein" arrived (Package 56).</summary>
    ServerMessage,
    /// <summary>A message in the own channel arrived (Package 56).</summary>
    ChannelMessage,
    /// <summary>Someone talks in over a link (Package 57).</summary>
    LinkVoice,
    /// <summary>The own user starts talking over a link (Package 60).</summary>
    OwnLinkVoice,
    /// <summary>Someone changed the own groups (Package 73).</summary>
    GroupChanged,
    /// <summary>The own user changed the own groups (Package 73).</summary>
    GroupChangedByMe,
    /// <summary>The talk key went down (Package 131, A140: off by default).</summary>
    TalkKeyDown,
    /// <summary>The talk key went up (Package 131, A140: off by default).</summary>
    TalkKeyUp,
}
