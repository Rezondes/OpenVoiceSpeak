namespace OVS.Shared.Protocol.Messages.Moderation;

/// <summary>Package 85: the stored server mute of a known user by fingerprint, online or offline; right UserMute and the rank rule.</summary>
public sealed record SetKnownUserServerMute(string Fingerprint, bool Muted) : Request;
