using OVS.Shared.Moderation;

namespace OVS.Server.Data.Records;

public sealed class BanRecord
{
    public Guid Id { get; set; }
    public string Fingerprint { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string? Ip { get; set; }
    public string Reason { get; set; } = "";
    public string CreatedBy { get; set; } = "";
    public DateTimeOffset? ExpiresAt { get; set; }

    // Package 80 (A97): details and history. Bans saved before start at null or 0, without a new data version.
    public DateTimeOffset? CreatedAt { get; set; }
    public string? CreatedByFingerprint { get; set; }
    /// <summary>The original duration; null = permanent (or unknown for an old ban with an end).</summary>
    public int? DurationMinutes { get; set; }
    public DateTimeOffset? LiftedAt { get; set; }
    /// <summary>Nickname of who lifted the ban.</summary>
    public string? LiftedBy { get; set; }
    public int BlockedAttempts { get; set; }
    public DateTimeOffset? LastAttempt { get; set; }
    public string? LastAttemptIp { get; set; }

    public bool IsActive(DateTimeOffset now) => BanStatusRule.Evaluate(LiftedAt, ExpiresAt, now) == BanStatus.Active;

    /// <summary>When a lifted or expired ban ended; null while active.</summary>
    public DateTimeOffset? EndedAt(DateTimeOffset now) => IsActive(now) ? null : LiftedAt ?? ExpiresAt;
}
