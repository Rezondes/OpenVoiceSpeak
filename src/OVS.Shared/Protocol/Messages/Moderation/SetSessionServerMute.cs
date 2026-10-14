namespace OVS.Shared.Protocol.Messages.Moderation;

/// <summary>The server mute of the session's current connection; it is stored on the user record by fingerprint like <see cref="SetKnownUserServerMute"/>, so the next login starts muted.</summary>
/// <param name="SessionId">The session to mute or unmute.</param>
/// <param name="Muted">Muted or not.</param>
/// <param name="Minutes">Package 166: the mute ends by itself after this many minutes (1 to 28 days); null lasts until lifted.</param>
public sealed record SetSessionServerMute(uint SessionId, bool Muted, int? Minutes = null) : Request;
