using OVS.Shared.Moderation;

namespace OVS.Server.Data.Records;

/// <summary>A ban as stored, with its history.</summary>
public sealed class BanRecord
{
    /// <summary>The ban's id.</summary>
    public Guid Id { get; set; }
    /// <summary>The banned identity.</summary>
    public string Fingerprint { get; set; } = "";
    /// <summary>The banned user's nickname at the time of the ban.</summary>
    public string Nickname { get; set; } = "";
    /// <summary>The banned address, when the ban covers it.</summary>
    public string? Ip { get; set; }
    /// <summary>The reason the moderator gave.</summary>
    public string Reason { get; set; } = "";
    /// <summary>The nickname of the moderator who banned.</summary>
    public string CreatedBy { get; set; } = "";
    /// <summary>When the ban ends; null = permanent.</summary>
    public DateTimeOffset? ExpiresAt { get; set; }

    // Package 80 (A97): details and history. Bans saved before start at null or 0, without a new data version.
    /// <summary>When the ban was made (Package 80, A97); null for bans saved before.</summary>
    public DateTimeOffset? CreatedAt { get; set; }
    /// <summary>The fingerprint of the moderator who banned.</summary>
    public string? CreatedByFingerprint { get; set; }
    /// <summary>The original duration; null = permanent (or unknown for an old ban with an end).</summary>
    public int? DurationMinutes { get; set; }
    /// <summary>When a moderator lifted the ban; null while it was not lifted.</summary>
    public DateTimeOffset? LiftedAt { get; set; }
    /// <summary>Nickname of who lifted the ban.</summary>
    public string? LiftedBy { get; set; }
    /// <summary>How often the ban turned someone away.</summary>
    public int BlockedAttempts { get; set; }
    /// <summary>The last time the ban turned someone away.</summary>
    public DateTimeOffset? LastAttempt { get; set; }
    /// <summary>The address of the last attempt the ban turned away.</summary>
    public string? LastAttemptIp { get; set; }

    /// <summary>Package 80 (A97): counts a join the ban turned away.</summary>
    public void CountBlockedAttempt(string ip, DateTimeOffset now)
    {
        BlockedAttempts++;
        LastAttempt = now;
        LastAttemptIp = ip;
    }

    /// <summary>True while the ban is neither lifted nor expired at now.</summary>
    public bool IsActive(DateTimeOffset now) => BanStatusRule.Evaluate(LiftedAt, ExpiresAt, now) == BanStatus.Active;

    /// <summary>When a lifted or expired ban ended; null while active.</summary>
    public DateTimeOffset? EndedAt(DateTimeOffset now) => IsActive(now) ? null : LiftedAt ?? ExpiresAt;
}
