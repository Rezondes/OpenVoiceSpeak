namespace OVS.Manager.Audit;

/// <summary>
/// Package M5 (AC1): one line of the audit log: when (UTC), who, what, on which account or server and a short detail.
/// Every field is set once when the line is written and never changed (init only); lines are only added and, with a
/// retention, deleted by age.
/// </summary>
internal sealed class AuditEntry
{
    /// <summary>The line's number, given by the database.</summary>
    public long Id { get; set; }

    /// <summary>When it happened, in UTC (shown in local time).</summary>
    public DateTime AtUtc { get; init; }

    /// <summary>The acting user's name, "system" for the manager itself.</summary>
    public string UserName { get; init; } = "";

    /// <summary>What was done, one of <see cref="AuditActions"/>.</summary>
    public string Action { get; init; } = "";

    /// <summary>The account or server acted on, by name; null when none.</summary>
    public string? Target { get; init; }

    /// <summary>The server's id when a server was acted on (for the managers' view, M10).</summary>
    public string? ServerId { get; init; }

    /// <summary>A short detail, such as "old -> new" or the address of a failed login.</summary>
    public string? Detail { get; init; }
}
