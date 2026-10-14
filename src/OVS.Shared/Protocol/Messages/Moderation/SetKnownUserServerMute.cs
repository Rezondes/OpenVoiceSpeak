namespace OVS.Shared.Protocol.Messages.Moderation;

/// <summary>Package 85: the stored server mute of a known user by fingerprint, online or offline; right UserMute and the rank rule.</summary>
/// <param name="Fingerprint">The user to mute or unmute.</param>
/// <param name="Muted">Muted or not.</param>
/// <param name="Minutes">Package 166: the mute ends by itself after this many minutes (1 to 28 days); null lasts until lifted.</param>
public sealed record SetKnownUserServerMute(string Fingerprint, bool Muted, int? Minutes = null) : Request;
