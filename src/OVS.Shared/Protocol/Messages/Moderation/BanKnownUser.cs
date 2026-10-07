namespace OVS.Shared.Protocol.Messages.Moderation;

/// <summary>Package 72: bans a known user by fingerprint, online or offline; IncludeIp uses the last known IP.</summary>
public sealed record BanKnownUser(string Fingerprint, string Reason, int? DurationMinutes, bool IncludeIp) : Request;
