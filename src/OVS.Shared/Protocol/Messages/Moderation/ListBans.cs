using OVS.Shared.Protocol.Messages.Users;

namespace OVS.Shared.Protocol.Messages.Moderation;

/// <param name="Offset">Package 87: paged like <see cref="ListUsers"/>.</param>
public sealed record ListBans(int Offset = 0) : Request;
