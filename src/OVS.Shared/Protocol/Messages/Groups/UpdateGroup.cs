using OVS.Shared.Permissions;

namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>Renames a group and sets its rights.</summary>
/// <param name="GroupId">The group to change.</param>
/// <param name="Name">The new name.</param>
/// <param name="Permissions">The new rights.</param>
/// <param name="Color">Package 167: the colour of its members' names; null for none.</param>
public sealed record UpdateGroup(Guid GroupId, string Name, Permission Permissions, GroupColor? Color = null) : Request;
