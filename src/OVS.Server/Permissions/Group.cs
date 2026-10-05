using OVS.Shared.Permissions;

namespace OVS.Server.Permissions;

public sealed record Group(Guid Id, string Name, Permission Permissions);
