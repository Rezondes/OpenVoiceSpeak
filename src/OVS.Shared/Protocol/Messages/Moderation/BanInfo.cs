namespace OVS.Shared.Protocol.Messages.Moderation;

/// <summary>Package 80 (A97): active, expired and lifted bans; the new values are null or 0 for bans saved before.</summary>
public sealed record BanInfo(
    Guid Id, string Fingerprint, string Nickname, string? Ip, string Reason, string CreatedBy, DateTimeOffset? ExpiresAt,
    DateTimeOffset? CreatedAt = null, string? CreatedByFingerprint = null, int? DurationMinutes = null,
    DateTimeOffset? LiftedAt = null, string? LiftedBy = null, int BlockedAttempts = 0, DateTimeOffset? LastAttempt = null, string? LastAttemptIp = null);
