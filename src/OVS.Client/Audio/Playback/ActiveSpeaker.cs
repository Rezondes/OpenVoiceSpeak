namespace OVS.Client.Audio.Playback;

/// <summary>Someone the mixer plays now, and whether they come in over a link.</summary>
internal readonly record struct ActiveSpeaker(uint SessionId, bool ViaLink);
