namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>
/// Logs (Package 81, A98: right LogsView).
/// Asks for one page of WireProtocol.LogPageLines lines; Page null = the last page.
/// </summary>
public sealed record ReadLog(string FileId, int? Page = null) : Request;
