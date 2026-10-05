namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>Logs (Package 81, A98: right LogsView). Newest first.</summary>
public sealed record LogList(string? RequestId, IReadOnlyList<LogFileInfo> Files, int Offset = 0, int Total = 0) : Message;
