using OVS.Shared.Permissions;

namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>Renames a group and sets its rights.</summary>
public sealed record UpdateGroup(Guid GroupId, string Name, Permission Permissions) : Request;
