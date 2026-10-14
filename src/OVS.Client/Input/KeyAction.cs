namespace OVS.Client.Input;

/// <summary>What a key can do. Hold actions last while the key is down, the others fire once per press.</summary>
internal enum KeyAction
{
    /// <summary>Talk while held.</summary>
    PushToTalk,
    /// <summary>Talk into the linked channels too while held.</summary>
    LinkPushToTalk,
    /// <summary>Mute while held.</summary>
    PushToMute,
    /// <summary>Mute or unmute.</summary>
    ToggleMute,
    /// <summary>Deafen or undeafen.</summary>
    ToggleDeafen,
    /// <summary>Package 160: streamer mode on or off, also without a connection.</summary>
    ToggleStreamerMode,
}
