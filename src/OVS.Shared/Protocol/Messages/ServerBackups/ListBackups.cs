using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Shared.Protocol.Messages.ServerBackups;

/// <summary>Backups (Package 74, right ServerConfig).</summary>
/// <param name="Offset">Package 87: paged like <see cref="ListUsers"/>.</param>
public sealed record ListBackups(int Offset = 0) : Request;
