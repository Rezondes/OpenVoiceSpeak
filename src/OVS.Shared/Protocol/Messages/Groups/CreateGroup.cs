using OVS.Shared.Permissions;

namespace OVS.Shared.Protocol.Messages.Groups;

/// <summary>Creates a group.</summary>
public sealed record CreateGroup(string Name, Permission Permissions) : Request;
