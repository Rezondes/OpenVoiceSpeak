using OVS.Shared.Permissions;

namespace OVS.Shared.Protocol.Messages.Groups;

public sealed record UpdateGroup(Guid GroupId, string Name, Permission Permissions) : Request;
