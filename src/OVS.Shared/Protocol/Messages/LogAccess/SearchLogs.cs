namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>
/// Logs (Package 81, A98: right LogsView).
/// Plain text, case-insensitive, over every file that passes the optional filters (Kind, ChannelId, period of the file).
/// </summary>
public sealed record SearchLogs(string Query, LogKind? Kind = null, Guid? ChannelId = null, DateTimeOffset? From = null, DateTimeOffset? To = null) : Request;
