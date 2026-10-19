namespace OVS.Manager.Audit;

/// <summary>Package M5 (AC3): what the audit page filters by; null means any.</summary>
/// <param name="UserName">The acting user.</param>
/// <param name="ServerId">The server acted on.</param>
/// <param name="Action">The action, one of <see cref="AuditActions"/>.</param>
/// <param name="FromUtc">The first time, inclusive.</param>
/// <param name="ToUtc">The last time, exclusive.</param>
internal sealed record AuditFilter(string? UserName = null, string? ServerId = null, string? Action = null, DateTime? FromUtc = null, DateTime? ToUtc = null);
