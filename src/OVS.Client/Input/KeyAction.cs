namespace OVS.Client.Input;

/// <summary>What a key can do. Hold actions last while the key is down, the others fire once per press.</summary>
public enum KeyAction { PushToTalk, LinkPushToTalk, PushToMute, ToggleMute, ToggleDeafen }
