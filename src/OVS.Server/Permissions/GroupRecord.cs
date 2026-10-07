using OVS.Shared.Permissions;

namespace OVS.Server.Permissions;

public sealed record GroupRecord(Guid Id, string Name, Permission Permissions);
