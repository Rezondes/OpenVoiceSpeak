namespace OVS.Client.Audio.Sounds;

/// <summary>Package 47 (A46): the moments the client gives an acoustic cue for.</summary>
public enum SoundEvent
{
    MicOff, MicOn, SoundOff, SoundOn,
    Connected, Disconnected,
    ChannelEntered, UserJoined, UserLeft,
    ServerMuted, Moved, PrivateMessage,
    ServerMessage, ChannelMessage, // Package 56
    LinkVoice, // Package 57
    OwnLinkVoice, // Package 60
    GroupChanged, GroupChangedByMe, // Package 73
    TalkKeyDown, TalkKeyUp, // Package 131 (A140): off by default
}
