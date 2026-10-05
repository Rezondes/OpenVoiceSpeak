using OVS.Shared.Permissions;

namespace OVS.Shared.Protocol.Messages.Groups;

public sealed record CreateGroup(string Name, Permission Permissions) : Request;
