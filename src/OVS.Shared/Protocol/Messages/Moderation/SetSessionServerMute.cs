namespace OVS.Shared.Protocol.Messages.Moderation;

/// <summary>The server mute of the session's current connection; it is stored on the user record by fingerprint like <see cref="SetKnownUserServerMute"/>, so the next login starts muted.</summary>
public sealed record SetSessionServerMute(uint SessionId, bool Muted) : Request;
