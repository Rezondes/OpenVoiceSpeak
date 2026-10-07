namespace OVS.Shared.Protocol.Messages.Moderation;

/// <summary>Bans the user of the session's current connection (IncludeIp uses its current address); the ban is stored by fingerprint like <see cref="BanKnownUser"/>.</summary>
public sealed record BanSession(uint SessionId, string Reason, int? DurationMinutes, bool IncludeIp) : Request;
