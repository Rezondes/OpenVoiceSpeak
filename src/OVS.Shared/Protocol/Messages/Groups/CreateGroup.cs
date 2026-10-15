using OVS.Shared.Permissions;

namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>Creates a group.</summary>
/// <param name="Name">The group's name.</param>
/// <param name="Permissions">The group's rights.</param>
/// <param name="Color">Package 167: the colour of its members' names; null for none.</param>
public sealed record CreateGroup(string Name, Permission Permissions, GroupColor? Color = null) : Request;
