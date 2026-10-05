using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Shared.Protocol.Messages.LogAccess;

/// <summary>Logs (Package 81, A98: right LogsView).</summary>
/// <param name="Offset">Package 87: paged like <see cref="ListUsers"/>.</param>
public sealed record ListLogs(int Offset = 0) : Request;
