namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>
/// Logs (Package 81, A98: right LogsView).
/// Newest first. Truncated: there were more than ProtocolLimits.MaxLogHits; TimedOut: the search stopped after its time limit.
/// </summary>
public sealed record LogSearchResult(string? RequestId, IReadOnlyList<LogHit> Hits, bool Truncated, bool TimedOut) : Message;
